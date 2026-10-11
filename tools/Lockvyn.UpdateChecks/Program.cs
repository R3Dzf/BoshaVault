using System.IO.Compression;
using System.Security.Cryptography;
using BoshaVault.Core;
using Velopack;
using Velopack.Locators;
using Velopack.Logging;
using Velopack.Sources;

if (args.Length == 2 && args[0] is "--create-vault" or "--verify-vault")
{
    const string passphrase = "OnlySyntheticUpdatePassphrase-2026!";
    const string device = "11111111-1111-4111-8111-111111111111";
    if (args[0] == "--create-vault")
    {
        if (File.Exists(args[1])) throw new Exception("Refusing to replace an existing vault");
        using var vault = await VaultSession.Create(args[1], passphrase, device);
        vault.Upsert(new VaultEntry { Title = "Update preservation", Url = "https://example.com", Username = "synthetic@example.com", Password = "OnlySyntheticUpdateSecret-7!", Notes = "Synthetic acceptance fixture" });
    }
    else
    {
        using var vault = await VaultSession.Open(args[1], passphrase, device);
        if (vault.Data.Entries.Count != 1 || vault.Data.Entries[0].Password != "OnlySyntheticUpdateSecret-7!") throw new Exception("Vault preservation failed");
    }
    Console.WriteLine("PASS: Synthetic encrypted vault remains usable"); return;
}
if (args.Length < 2) throw new ArgumentException("Usage: update-checks <feed> <test-root> [--apply-installed]");
string feedDir = Path.GetFullPath(args[0]), testRoot = Path.GetFullPath(args[1]);
var feed = VelopackAssetFeed.FromJson(File.ReadAllText(Path.Combine(feedDir, "releases.win.json")));
var baseline = feed.Assets.Single(a => a.Type == VelopackAssetType.Full && a.Version.ToString() == "9.0.0");
var target = feed.Assets.Single(a => a.Type == VelopackAssetType.Full && a.Version.ToString() == "9.0.1");
int checks = 0;
void Check(bool condition, string message) { if (!condition) throw new Exception(message); Console.WriteLine("PASS: " + message); checks++; }
void SameContent(string actual, string expected)
{
    using var a = ZipFile.OpenRead(actual); using var b = ZipFile.OpenRead(expected);
    var aFiles = a.Entries.Where(e => !e.FullName.EndsWith('/')).ToDictionary(e => e.FullName);
    var bFiles = b.Entries.Where(e => !e.FullName.EndsWith('/')).ToDictionary(e => e.FullName);
    Check(aFiles.Keys.Order().SequenceEqual(bFiles.Keys.Order()), "Reconstructed package contains every target file");
    Check(bFiles.All(p => { using var s1 = p.Value.Open(); using var s2 = aFiles[p.Key].Open(); return SHA256.HashData(s1).SequenceEqual(SHA256.HashData(s2)); }), "Every reconstructed file matches the target SHA-256");
}
if (args.Contains("--apply-installed"))
{
    var locator = new RealTestLocator(testRoot, "9.0.0");
    var source = new TrackedSource(feedDir);
    var manager = new UpdateManager(source, new UpdateOptions { ExplicitChannel = "win" }, locator);
    var update = await manager.CheckForUpdatesAsync() ?? throw new Exception("Installed update not found");
    await manager.DownloadUpdatesAsync(update);
    Check(source.Downloads.Any(x => x.Contains("-delta.nupkg")), "Installed baseline uses the delta download");
    Check(!source.Downloads.Any(x => x.EndsWith("-full.nupkg")), "Installed baseline avoids downloading the full release");
    SameContent(Path.Combine(locator.PackagesDir!, target.FileName), Path.Combine(feedDir, target.FileName));
    manager.WaitExitThenApplyUpdates(target, silent: true, restart: false, restartArgs: []);
    Console.WriteLine("Updater scheduled; exiting normally."); return;
}
foreach (var scenario in new[] { "delta", "missing-base", "damaged-delta", "cancelled", "damaged-full" })
{
    string root = Path.Combine(testRoot, scenario), packages = Path.Combine(root, "packages"); Directory.CreateDirectory(packages);
    // Use the real Windows patcher from the trusted fixture package. The SDK
    // falls back to a full download if Update.exe is absent, even with a valid delta.
    string current = Path.Combine(root, "current"); Directory.CreateDirectory(current);
    var updater = new Velopack.NuGet.ZipPackage(Path.Combine(feedDir, baseline.FileName), loadUpdateExe: true).UpdateExeBytes
        ?? throw new Exception("Fixture package contains no Windows updater");
    File.WriteAllBytes(Path.Combine(root, "Update.exe"), updater);
    using (var package = ZipFile.OpenRead(Path.Combine(feedDir, baseline.FileName)))
    {
        package.Entries.Single(e => e.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase)).ExtractToFile(Path.Combine(current, "sq.version"), true);
    }
    if (scenario is "delta" or "damaged-delta" or "cancelled") File.Copy(Path.Combine(feedDir, baseline.FileName), Path.Combine(packages, baseline.FileName), true);
    var source = new TrackedSource(feedDir) { DamageDelta = scenario == "damaged-delta", DamageFull = scenario == "damaged-full" };
    var locator = new TestVelopackLocator("Lockvyn", "9.0.0", packages, current, root, Path.Combine(root, "Update.exe"), "win", new FixtureLogger());
    var manager = new UpdateManager(source, new UpdateOptions { ExplicitChannel = "win" }, locator);
    var update = await manager.CheckForUpdatesAsync() ?? throw new Exception("Update not found");
    Check(update.TargetFullRelease.Version.ToString() == "9.0.1" && !update.IsDowngrade, scenario + ": upgrade target is correct");
    if (scenario == "cancelled")
    {
        using var cancel = new CancellationTokenSource(); cancel.Cancel(); bool rejected = false;
        try { await manager.DownloadUpdatesAsync(update, cancelToken: cancel.Token); } catch (OperationCanceledException) { rejected = true; }
        Check(rejected && manager.UpdatePendingRestart == null, "Cancelled download cannot become an installable update"); continue;
    }
    if (scenario == "damaged-full")
    {
        bool rejected = false;
        try { await manager.DownloadUpdatesAsync(update); } catch (Exception) { rejected = true; }
        Check(rejected && manager.UpdatePendingRestart == null, "Damaged full release is rejected and cannot be installed"); continue;
    }
    await manager.DownloadUpdatesAsync(update);
    Check(manager.UpdatePendingRestart?.Version == target.Version, scenario + ": verified update is ready");
    if (scenario == "delta") Check(source.Downloads.Any(x => x.Contains("-delta.nupkg")) && !source.Downloads.Any(x => x.EndsWith("-full.nupkg")), "A valid delta avoids a full download");
    if (scenario == "missing-base") Check(source.Downloads.Contains(target.FileName), "Missing local baseline falls back to full release");
    if (scenario == "damaged-delta") Check(source.Downloads.Any(x => x.Contains("-delta.nupkg")) && source.Downloads.Contains(target.FileName), "Damaged delta falls back to a verified full release");
    SameContent(Path.Combine(packages, target.FileName), Path.Combine(feedDir, target.FileName));
    Console.WriteLine($"DOWNLOAD {scenario}: {source.BytesRequested} bytes; full package {target.Size} bytes");
}
foreach (string installed in new[] { "9.0.1", "9.0.2" })
{
    string p = Path.Combine(testRoot, "version-" + installed); Directory.CreateDirectory(p);
    var locator = new TestVelopackLocator("Lockvyn", installed, p, p, p, Path.Combine(p, "Update.exe"), "win");
    var manager = new UpdateManager(new SimpleFileSource(new DirectoryInfo(feedDir)), new UpdateOptions { ExplicitChannel = "win" }, locator);
    Check(await manager.CheckForUpdatesAsync() == null, "Same/older releases do not replace version " + installed);
}
Console.WriteLine($"RESULT: {checks} update package checks passed.");

sealed class TrackedSource(string directory) : IUpdateSource
{
    private readonly SimpleFileSource source = new(new DirectoryInfo(directory));
    public bool DamageDelta { get; init; }
    public bool DamageFull { get; init; }
    public List<string> Downloads { get; } = [];
    public long BytesRequested { get; private set; }
    public Task<VelopackAssetFeed> GetReleaseFeed(IVelopackLogger logger, string? appId, string channel, Guid? stagingId = null, VelopackAsset? latestLocalRelease = null) => source.GetReleaseFeed(logger, appId, channel, stagingId, latestLocalRelease);
    public async Task DownloadReleaseEntry(IVelopackLogger logger, VelopackAsset releaseEntry, string localFile, Action<int> progress, CancellationToken cancelToken = default)
    {
        Downloads.Add(releaseEntry.FileName); BytesRequested += releaseEntry.Size;
        await source.DownloadReleaseEntry(logger, releaseEntry, localFile, progress, cancelToken);
        if ((DamageDelta && releaseEntry.Type == VelopackAssetType.Delta) || (DamageFull && releaseEntry.Type == VelopackAssetType.Full))
        { using var file = new FileStream(localFile, FileMode.Open, FileAccess.ReadWrite); file.Position = file.Length / 2; int value = file.ReadByte(); file.Position--; file.WriteByte((byte)(value ^ 1)); }
    }
}
sealed class FixtureLogger : IVelopackLogger
{
    public void Log(VelopackLogLevel level, string? message, Exception? exception = null)
    { if (exception != null) Console.WriteLine($"SDK {level}: {message} ({exception.Message})"); }
}
sealed class RealTestLocator : VelopackLocator
{
    private readonly string root;
    public RealTestLocator(string root, string version) { this.root = root; CurrentlyInstalledVersion = SemanticVersion.Parse(version); }
    public override string AppId => "Lockvyn";
    public override string RootAppDir => root;
    public override string PackagesDir => Path.Combine(root, "packages");
    public override string UpdateExePath => Path.Combine(root, "Update.exe");
    public override SemanticVersion CurrentlyInstalledVersion { get; }
    public override string AppContentDir => Path.Combine(root, "current");
    public override string Channel => "win";
    public override IVelopackLogger Log { get; } = new NullVelopackLogger();
    public override IProcessImpl Process => new DefaultProcessImpl(Log);
}
