using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using BoshaVault.Core;

string temp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "boshavault-tests-" + Guid.NewGuid());
Directory.CreateDirectory(temp);
string devA = "11111111-1111-1111-1111-111111111111", devB = "22222222-2222-2222-2222-222222222222";
string password = "correct horse battery staple café 🔑";
int passed = 0;
void Check(bool value, string name) { if (!value) throw new Exception("FAIL: " + name); Console.WriteLine("PASS " + name); passed++; }
async Task Reject(Func<Task> action, string name) { bool rejected = false; try { await action(); } catch (Exception ex) when (ex is VaultException or IOException or JsonException or InvalidDataException) { rejected = true; } Check(rejected, name); }
void RejectNow(Action action, string name) { bool rejected = false; try { action(); } catch (Exception ex) when (ex is VaultException or IOException or JsonException or FormatException) { rejected = true; } Check(rejected, name); }
VaultData Clone(VaultData d) => JsonSerializer.Deserialize<VaultData>(JsonSerializer.Serialize(d, JsonOptions.Strict), JsonOptions.Strict)!;
try
{
    if (args.Length > 0 && args[0] == "--desktop-tests") { await DesktopTests.Run(); return; }
    if (args.Length > 0 && args[0] == "--activation-client") { Environment.ExitCode = await BoshaVault.Windows.InstanceActivationChannel.RequestOpen(args[1]) ? 0 : 1; return; }
    if (args.Length > 0 && args[0] == "--tls-handshake-tests") { await TlsHandshakeTests.Run(); return; }
    if (args.Length > 0 && args[0] == "--tls-certificate-tests")
    {
        using var firstCertificate = TemporaryTlsCertificate.Create(); using var secondCertificate = TemporaryTlsCertificate.Create();
        Check(firstCertificate.HasPrivateKey, "Temporary pairing certificate retains its signing key");
        byte[] message = RandomNumberGenerator.GetBytes(32);
        using var privateKey = firstCertificate.GetRSAPrivateKey()!; using var publicKey = firstCertificate.GetRSAPublicKey()!;
        Check(publicKey.VerifyData(message, privateKey.SignData(message, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1), "Imported pairing certificate key actually signs and verifies");
        Check(!SHA256.HashData(firstCertificate.RawData).SequenceEqual(SHA256.HashData(secondCertificate.RawData)), "A new pairing session gets a new certificate pin");
        Check(firstCertificate.NotAfter.ToUniversalTime() > DateTime.UtcNow.AddMinutes(4) && firstCertificate.NotAfter.ToUniversalTime() <= DateTime.UtcNow.AddMinutes(5.1), "Pairing certificate has a short bounded lifetime");
        Console.WriteLine($"RESULT: {passed} temporary TLS certificate checks passed; Windows Schannel handshake still requires a native test."); return;
    }
    if (args.Length > 0 && args[0] == "--qr-fixtures")
    {
        string Url64(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        Directory.CreateDirectory(args[1]);
        foreach (string host in new[] { "192.168.1.24", "10.42.0.1", "172.31.255.254" })
        {
            string raw = JsonSerializer.Serialize(new { v = 1, host, port = 45678, cert = new string('a', 64), token = Url64(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray()) });
            string code = "BV1:" + Url64(Encoding.UTF8.GetBytes(raw));
            File.WriteAllText(Path.Combine(args[1], host + ".txt"), code);
            File.WriteAllBytes(Path.Combine(args[1], host + ".png"), PairingQr.Render(code));
        }
        Console.WriteLine("Synthetic pairing QR fixtures generated."); return;
    }
    if (args.Length > 0 && args[0] == "--verify-java")
    {
        using var j = await VaultSession.Open(args[1], password, devA);
        Check(j.Data.Entries.Any(e=>e.Password=="OnlySyntheticTestSecret-7!") && j.Data.Entries.Any(e=>e.Title=="Java demo"), "Java-created encrypted file reopens in .NET");
        return;
    }
    if (args.Length > 0 && args[0] == "--lan-host")
    {
        string exchange = args[1]; string sourcePath = temp + "/server.boshavault";
        File.Copy(exchange + "/dotnet.boshavault", sourcePath);
        using var hostSession = await VaultSession.Open(sourcePath, password, devA);
        var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var server = new LocalTransferServer(hostSession.ExportEncrypted, bytes => { hostSession.MergeEncrypted(bytes); received.TrySetResult(); });
        File.WriteAllText(exchange + "/pairing.txt", server.Start(args.Length > 2 ? args[2] : null));
        Console.WriteLine("LAN test host ready.");
        if (await Task.WhenAny(received.Task, Task.Delay(TimeSpan.FromSeconds(50))) != received.Task) throw new Exception("LAN test timed out.");
        Check(hostSession.Data.Entries.Any(e => e.Title == "LAN test"), "real pinned TLS Java → .NET encrypted merge");
        await Task.Delay(300); File.Delete(exchange + "/pairing.txt");
        return;
    }
    string path = temp + "/a.boshavault";
    using var a = await VaultSession.Create(path, password, devA);
    var entry = new VaultEntry { Title = "Demo Mail", Username = "demo@example.com", Password = "OnlySyntheticTestSecret-7!", Url = "https://example.com/login", Notes = "ملاحظات تجريبية", Favorite = true };
    a.Upsert(entry);
    byte[] first = a.ExportEncrypted();
    string wire = Encoding.UTF8.GetString(first);
    Check(!wire.Contains(entry.Password) && !wire.Contains(entry.Title) && !wire.Contains(entry.Username) && !wire.Contains(entry.Notes), "all sensitive metadata encrypted");
    using (var opened = await VaultSession.Open(path, password, devB))
        Check(opened.Data.Entries.Single().Password == entry.Password && opened.Data.Entries.Single().Notes == entry.Notes, "create / save / reopen Unicode round trip");
    await Reject(async () => { using var s = await VaultSession.Open(path, "wrong passphrase", devB); }, "wrong master passphrase rejected");
    var parsed = VaultCodec.Parse(first);
    var payload = Convert.FromBase64String(parsed.Payload.Ciphertext); payload[0] ^= 1; parsed.Payload.Ciphertext = Convert.ToBase64String(payload);
    string bad = temp + "/tampered.boshavault"; File.WriteAllBytes(bad, JsonSerializer.SerializeToUtf8Bytes(parsed, JsonOptions.Strict));
    await Reject(async () => { using var s = await VaultSession.Open(bad, password, devB); }, "ciphertext modification rejected");
    parsed = VaultCodec.Parse(first); parsed.VaultId = Guid.NewGuid().ToString(); File.WriteAllBytes(bad, JsonSerializer.SerializeToUtf8Bytes(parsed, JsonOptions.Strict));
    await Reject(async () => { using var s = await VaultSession.Open(bad, password, devB); }, "header identity substitution rejected");
    parsed = VaultCodec.Parse(first); parsed.Kdf.MemoryKiB = int.MaxValue;
    RejectNow(() => VaultCodec.Parse(JsonSerializer.SerializeToUtf8Bytes(parsed, JsonOptions.Strict)), "hostile KDF memory request rejected before allocation");
    parsed.Kdf.MemoryKiB = 1024;
    RejectNow(() => VaultCodec.Parse(JsonSerializer.SerializeToUtf8Bytes(parsed, JsonOptions.Strict)), "KDF downgrade rejected");
    RejectNow(() => VaultCodec.Parse(Encoding.UTF8.GetBytes(wire.Replace("\"format\":\"BoshaVault\"", "\"format\":\"BoshaVault\",\"format\":\"BoshaVault\""))), "duplicate JSON properties rejected");
    RejectNow(() => VaultCodec.Parse(first[..(first.Length / 2)]), "truncated encrypted backup rejected");
    RejectNow(() => VaultCodec.Parse(new byte[VaultCodec.MaxFileBytes + 1]), "oversized input rejected");
    parsed = VaultCodec.Parse(first); parsed.Version = 99;
    RejectNow(() => VaultCodec.Parse(JsonSerializer.SerializeToUtf8Bytes(parsed, JsonOptions.Strict)), "unknown format version rejected");
    parsed = VaultCodec.Parse(first); parsed.Payload.Tag = Convert.ToBase64String(new byte[12]);
    RejectNow(() => VaultCodec.Parse(JsonSerializer.SerializeToUtf8Bytes(parsed, JsonOptions.Strict)), "invalid authentication tag size rejected");
    string nonce = VaultCodec.Parse(first).Payload.Nonce;
    a.Upsert(a.Data.Entries.Single().Clone());
    Check(VaultCodec.Parse(a.ExportEncrypted()).Payload.Nonce != nonce, "fresh payload nonce on every write");
    string pathB = temp + "/b.boshavault"; File.WriteAllBytes(pathB, a.ExportEncrypted());
    using var b = await VaultSession.Open(pathB, password, devB);
    var changeA = a.Data.Entries.Single().Clone(); changeA.Password = "Synthetic-Branch-A-Only!"; a.Upsert(changeA);
    var changeB = b.Data.Entries.Single().Clone(); changeB.Password = "Synthetic-Branch-B-Only!"; b.Upsert(changeB);
    Check(a.MergeEncrypted(b.ExportEncrypted()) == 1 && a.Data.Entries.Count == 2, "concurrent offline edits preserve both passwords");
    Check(a.Data.Entries.Any(e => e.Password == changeA.Password) && a.Data.Entries.Any(e => e.Password == changeB.Password), "conflict contents preserved");
    Check(a.MergeEncrypted(b.ExportEncrypted()) == 0 && a.Data.Entries.Count == 2, "repeated merge does not duplicate conflicts");
    b.MergeEncrypted(a.ExportEncrypted());
    Check(b.Data.Entries.Count == 2 && b.Data.Entries.Select(e=>e.Id).ToHashSet().SetEquals(a.Data.Entries.Select(e=>e.Id)), "bidirectional merge converges on entry identities");
    var old = a.Data.Entries.First().Clone(); var deleted = old.Clone(); deleted.Deleted = true; a.Upsert(deleted);
    b.MergeEncrypted(a.ExportEncrypted()); Check(b.Data.Entries.Single(e=>e.Id==old.Id).Deleted, "tombstones synchronize deletion");
    var restore = b.Data.Entries.Single(e=>e.Id==old.Id).Clone(); restore.Deleted = false; b.Upsert(restore); a.MergeEncrypted(b.ExportEncrypted());
    Check(!a.Data.Entries.Single(e=>e.Id==old.Id).Deleted, "trash restore synchronizes");
    using var other = await VaultSession.Create(temp + "/other.boshavault", password, devB);
    RejectNow(() => a.MergeEncrypted(other.ExportEncrypted()), "different vault import cannot replace current vault");
    byte[] before = a.ExportEncrypted(); RejectNow(() => a.MergeEncrypted(first[..80]), "bad import is rejected");
    Check(before.SequenceEqual(a.ExportEncrypted()), "failed import preserves existing file exactly");
    using var stale = await VaultSession.Open(path, password, devA);
    a.Upsert(a.Data.Entries.First().Clone());
    RejectNow(() => stale.Upsert(stale.Data.Entries.First().Clone()), "stale session cannot overwrite newer changes");
    await Reject(() => a.ChangePassword("wrong-current", "new strong passphrase 12345"), "password change requires current passphrase");
    await a.ChangePassword(password, "new strong passphrase 12345");
    var oldEnv = VaultCodec.Parse(before); byte[] oldKek = await VaultCodec.DeriveKey(password, oldEnv.Kdf); byte[] oldKey = VaultCodec.Decrypt(oldKek, oldEnv.Wrap, VaultCodec.WrapAad(oldEnv));
    RejectNow(() => VaultCodec.OpenBody(VaultCodec.Parse(a.ExportEncrypted()), oldKey), "old decrypted vault key cannot open data after rotation");
    CryptographicOperations.ZeroMemory(oldKek); CryptographicOperations.ZeroMemory(oldKey);
    Check(VaultCodec.Parse(a.ExportEncrypted()).KeyEpoch == 2, "key epoch advances on password change");
    await Reject(async () => { using var s = await VaultSession.Open(path, password, devB); }, "old master passphrase fails against changed vault");
    using (var reopened = await VaultSession.Open(path, "new strong passphrase 12345", devB)) Check(reopened.Data.Entries.Count == a.Data.Entries.Count, "password change preserves entries");
    Check(!File.Exists(path + ".previous"), "previous local snapshot removed after password change");
    await b.MergeUsingPassword(a.ExportEncrypted(), "new strong passphrase 12345");
    using (var updatedDevice = await VaultSession.Open(pathB, "new strong passphrase 12345", devB)) Check(updatedDevice.Data.Entries.Count == a.Data.Entries.Count, "another device verifies and adopts rotated key without losing entries");
    await a.MergeUsingPassword(before, password);
    Check(VaultCodec.Parse(a.ExportEncrypted()).KeyEpoch == 2, "merging an old backup cannot downgrade current key epoch");
    Check(OriginPolicy.Matches("https://accounts.example.com/login", "https://accounts.example.com/another") && !OriginPolicy.Matches("https://example.com", "https://example.com.evil.test") && !OriginPolicy.Matches("https://example.com", "https://example-login.com") && !OriginPolicy.Matches("https://example.com", "http://example.com"), "origin policy rejects lookalikes / insecure scheme / different subdomain");
    Check(!OriginPolicy.Matches("https://example.com", "https://evil.test@example.com") && !OriginPolicy.Matches("https://example.com", "https://example.com:8443"), "embedded credentials and custom ports rejected");
    Check(BrowserAutofillProtocol.Validate(new BrowserAutofillRequest { Op="list", Origin="https://accounts.example.com" })=="accounts.example.com",
        "native browser requests accept exact HTTPS origin");
    foreach (string invalid in new[] { "http://accounts.example.com", "https://evil.test@accounts.example.com",
        "https://accounts.example.com:8080", "https://accounts.example.com.evil.test/path", "https://accounts.example.com?q=1",
        "file:///etc/passwd", "https://accounts.example.com/#section" })
        RejectNow(()=>BrowserAutofillProtocol.Validate(new BrowserAutofillRequest { Op="list", Origin=invalid }),
            "unsafe browser origin rejected");
    RejectNow(()=>BrowserAutofillProtocol.Validate(new BrowserAutofillRequest { Op="fill", Origin="https://accounts.example.com", EntryId="invalid" }),
        "unrecognized login identity rejected");

    Check(BrowserAutofillProtocol.Validate(new BrowserAutofillRequest {
        Op = "save", Origin = "https://github.com", Username = "example@email.test",
        Password = "RandomStrongPassword24!" }) == "github.com",
        "browser signup saving accepts reviewed HTTPS credentials");
    using (var pipeData=new MemoryStream())
    {
        await BrowserAutofillProtocol.WriteAsync(pipeData,new BrowserAutofillRequest { Op="list",Origin="https://accounts.example.com" },CancellationToken.None);
        pipeData.Position=0;
        var decoded=await BrowserAutofillProtocol.ReadAsync<BrowserAutofillRequest>(pipeData,CancellationToken.None);
        Check(decoded.Op=="list"&&decoded.Origin=="https://accounts.example.com","binary native messaging frame roundtrip");
    }
    await Reject(async()=> {
        using var tooBig=new MemoryStream();
        tooBig.Write(BitConverter.GetBytes(BrowserAutofillProtocol.MaxBytes+1));
        tooBig.Position=0;
        await BrowserAutofillProtocol.ReadAsync<BrowserAutofillRequest>(tooBig,CancellationToken.None);
    }, "oversized native message rejected");
    var generated = Enumerable.Range(0, 1000).Select(_ => PasswordGenerator.Generate(24)).ToList();
    Check(generated.Distinct().Count() == 1000 && generated.All(p => p.Length == 24 && p.Any(char.IsLower) && p.Any(char.IsUpper) && p.Any(char.IsDigit) && p.Any(c=>!char.IsLetterOrDigit(c))), "random generator length / required groups / collision sample");
    a.Dispose(); RejectNow(() => a.ExportEncrypted(), "locked session cannot export or read secrets");
    Check(a.Data.Entries.Count == 0, "lock removes plaintext references from model");
    // Public interoperability fixtures contain fake credentials only.
    if (args.Length > 0 && args[0] == "--fixtures")
    {
        string folder = args[1]; Directory.CreateDirectory(folder);
        string fixture = System.IO.Path.Combine(folder, "dotnet.boshavault"); if (File.Exists(fixture)) File.Delete(fixture);
        using var f = await VaultSession.Create(fixture, password, devA);
        f.Upsert(entry);
        var source = Clone(f.Data); var ra = Clone(source); var rb = Clone(source);
        ra.Entries[0].Password = "Synthetic-A"; MergeEngine.Tick(ra.Entries[0].Clock, devA);
        rb.Entries[0].Password = "Synthetic-B"; MergeEngine.Tick(rb.Entries[0].Clock, devB);
        MergeEngine.Merge(ra, rb, devA);
        File.WriteAllText(folder + "/merge-expected.json", JsonSerializer.Serialize(ra, JsonOptions.Strict));
        File.WriteAllText(folder + "/merge-base.json", JsonSerializer.Serialize(source, JsonOptions.Strict));
    }
    if (args.Length > 0 && args[0] == "--verify-java")
    {
        using var j = await VaultSession.Open(args[1], password, devA);
        Check(j.Data.Entries.Any(e=>e.Password=="OnlySyntheticTestSecret-7!") && j.Data.Entries.Any(e=>e.Title=="Java demo"), "Java-created encrypted file reopens in .NET");
    }
    Console.WriteLine($"RESULT: {passed} checks passed.");
}
finally { Directory.Delete(temp, true); }
