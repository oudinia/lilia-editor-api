using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Amazon.S3;
using Amazon.S3.Model;
using Lilia.Api.Services;
using Xunit;

namespace Lilia.Api.Tests.Integration.Infrastructure;

/// <summary>
/// A local S3 server standing in for Cloudflare R2 (21 Nov 2026, uploads move to a private R2 bucket): versitygw, an S3
/// gateway over a folder, in a container on 127.0.0.1 at a free port, started for a test class and removed after it.
/// MinIO's images are no longer published; versitygw speaks the same S3 (SigV4, path-style, presigned URLs, ETag =
/// MD5). Region "auto", as R2. Docker is required: a missing Docker fails the class, it never skips it.
/// </summary>
public sealed class LocalS3Server : IAsyncLifetime
{
    public const string Image = "versity/versitygw:v1.0.14";
    public const string AccessKey = "lilia-test-key";
    public const string SecretKey = "lilia-test-secret-not-real";

    private readonly string _name = $"lilia-s3-test-{Environment.ProcessId}-{Guid.NewGuid():N}"[..40];
    private string _data = null!;

    public string Endpoint { get; private set; } = null!;
    public IAmazonS3 Client { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        _data = Directory.CreateTempSubdirectory("lilia-s3-test-").FullName;
        var port = FreePort();
        var uid = Run("id", "-u").Trim();
        var gid = Run("id", "-g").Trim();
        Run("docker", "run", "-d", "--rm", "--name", _name, "--user", $"{uid}:{gid}",
            "-p", $"127.0.0.1:{port}:7070", "-v", $"{_data}:/data", Image,
            "--access", AccessKey, "--secret", SecretKey, "--region", "auto", "--quiet", "posix", "/data");
        Endpoint = $"http://127.0.0.1:{port}";
        Client = new AmazonS3Client(AccessKey, SecretKey, R2StorageService.ClientConfig(Endpoint, null));

        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (true)
        {
            try { await Client.ListBucketsAsync(); break; }
            catch when (DateTime.UtcNow < deadline) { await Task.Delay(250); }
        }
    }

    public async Task<string> NewBucketAsync()
    {
        var name = $"t-{Guid.NewGuid():N}"[..20];
        await Client.PutBucketAsync(new PutBucketRequest { BucketName = name });
        return name;
    }

    public R2StorageService Storage(string bucket, StorageOpsMeter? meter = null) =>
        new(Client, bucket, "/uploads", https: false, meter);

    public Task DisposeAsync()
    {
        try { Run("docker", "rm", "-f", _name); } catch { /* already gone */ }
        try { Directory.Delete(_data, recursive: true); } catch { /* files the container wrote as another user */ }
        Client?.Dispose();
        return Task.CompletedTask;
    }

    private static int FreePort()
    {
        using var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        return ((IPEndPoint)l.LocalEndpoint).Port;
    }

    private static string Run(string file, params string[] args)
    {
        var psi = new ProcessStartInfo(file) { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0) throw new InvalidOperationException($"{file} {args.FirstOrDefault()} failed: {stderr.Trim()}");
        return stdout;
    }
}
