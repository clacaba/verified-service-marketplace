using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;

namespace Marketplace.Api.Verification;

public interface IEvidenceStore
{
    Task Put(string key, byte[] bytes, CancellationToken ct);
    Task<byte[]> Get(string key, CancellationToken ct);
    Task Delete(string key, CancellationToken ct);
}
public interface IEvidenceScanner { Task<bool> IsClean(byte[] bytes, CancellationToken ct); }
public sealed class EvidenceUnavailableException : Exception { }
public sealed class S3EvidenceStore(IConfiguration config, IWebHostEnvironment environment) : IEvidenceStore
{
    private AmazonS3Client Client()
    {
        var endpoint = config["Storage:Endpoint"];
        var access = config["Storage:AccessKey"]; var secret = config["Storage:SecretKey"];
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || string.IsNullOrWhiteSpace(access) || string.IsNullOrWhiteSpace(secret) || (!environment.IsDevelopment() && uri.Scheme != "https")) throw new EvidenceUnavailableException();
        return new AmazonS3Client(new BasicAWSCredentials(access, secret), new AmazonS3Config { ServiceURL = endpoint, ForcePathStyle = true, AuthenticationRegion = "us-east-1", Timeout = TimeSpan.FromSeconds(30), MaxErrorRetry = 1 });
    }
    private string Bucket => config["Storage:Bucket"] ?? "verification-evidence";
    private async Task CheckPrivate(AmazonS3Client client, CancellationToken ct)
    {
        try { await client.GetBucketLocationAsync(new GetBucketLocationRequest { BucketName = Bucket }, ct); }
        catch (AmazonS3Exception e) when (e.StatusCode == HttpStatusCode.NotFound && environment.IsDevelopment())
        {
            try { await client.PutBucketAsync(new PutBucketRequest { BucketName = Bucket }, ct); }
            catch (AmazonS3Exception race) when (race.ErrorCode == "BucketAlreadyOwnedByYou") { }
        }
        try
        {
            var policy = await client.GetBucketPolicyAsync(new GetBucketPolicyRequest { BucketName = Bucket }, ct);
            PrivateBucketPolicy.Validate(policy.HttpStatusCode, policy.Policy);
        }
        catch (AmazonS3Exception e) when (e.StatusCode == HttpStatusCode.NotFound && e.ErrorCode == "NoSuchBucketPolicy") { }
    }
    public async Task Put(string key, byte[] bytes, CancellationToken ct)
    {
        using var client = Client(); await CheckPrivate(client, ct);
        using var stream = new MemoryStream(bytes, false);
        await client.PutObjectAsync(new PutObjectRequest { BucketName = Bucket, Key = key, InputStream = stream, ContentType = "application/octet-stream", AutoCloseStream = false }, ct);
    }
    public async Task<byte[]> Get(string key, CancellationToken ct)
    {
        using var client = Client(); await CheckPrivate(client, ct);
        using var response = await client.GetObjectAsync(new GetObjectRequest { BucketName = Bucket, Key = key }, ct);
        if (response.ContentLength > EvidenceOptions.MaxBytes) throw new EvidenceUnavailableException();
        using var output = new MemoryStream(); var buffer = new byte[8192]; int count;
        while ((count = await response.ResponseStream.ReadAsync(buffer, ct)) > 0)
        {
            if (output.Length + count > EvidenceOptions.MaxBytes) throw new EvidenceUnavailableException();
            await output.WriteAsync(buffer.AsMemory(0, count), ct);
        }
        return output.ToArray();
    }
    public async Task Delete(string key, CancellationToken ct)
    {
        using var client = Client();
        await client.DeleteObjectAsync(new DeleteObjectRequest { BucketName = Bucket, Key = key }, ct);
    }
}
public sealed class ClamEvidenceScanner(IConfiguration config) : IEvidenceScanner
{
    public async Task<bool> IsClean(byte[] bytes, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(45));
        using var client = new TcpClient();
        await client.ConnectAsync(config["Scanner:Host"] ?? "localhost", config.GetValue("Scanner:Port", 3310), deadline.Token);
        using var stream = client.GetStream();
        await stream.WriteAsync(Encoding.ASCII.GetBytes("zINSTREAM\0"), deadline.Token);
        var length = new byte[4];
        for (var offset = 0; offset < bytes.Length; offset += 65536)
        {
            var count = Math.Min(65536, bytes.Length - offset); BinaryPrimitives.WriteUInt32BigEndian(length, (uint)count);
            await stream.WriteAsync(length, deadline.Token); await stream.WriteAsync(bytes.AsMemory(offset, count), deadline.Token);
        }
        await stream.WriteAsync(new byte[4], deadline.Token);
        using var reply = new MemoryStream(); var single = new byte[1];
        var terminated = false;
        while (reply.Length < 4096 && await stream.ReadAsync(single, deadline.Token) == 1)
        {
            if (single[0] == 0) { terminated = true; break; }
            reply.WriteByte(single[0]);
        }
        if (!terminated) throw new EvidenceUnavailableException();
        var result = Encoding.UTF8.GetString(reply.ToArray());
        if (result == "stream: OK") return true;
        if (result.EndsWith(" FOUND", StringComparison.Ordinal)) return false;
        throw new EvidenceUnavailableException();
    }
}
public static class EvidenceFileValidation
{
    public static string? Detect(byte[] bytes)
    {
        if (bytes.Length >= 20 && bytes.AsSpan().StartsWith("%PDF-"u8) && Encoding.ASCII.GetString(bytes.AsSpan(Math.Max(0, bytes.Length - 1024))).TrimEnd().EndsWith("%%EOF", StringComparison.Ordinal)) return "application/pdf";
        if (bytes.Length >= 12 && bytes[0] == 255 && bytes[1] == 216 && bytes[2] == 255 && bytes[^2] == 255 && bytes[^1] == 217) return "image/jpeg";
        if (bytes.Length >= 33 && bytes.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) && bytes.AsSpan(bytes.Length - 12).SequenceEqual(new byte[] { 0, 0, 0, 0, 73, 69, 78, 68, 174, 66, 96, 130 })) return "image/png";
        return null;
    }
    public static string SafeName(string name, string contentType)
    {
        var leaf = name.Replace('\\', '/').Split('/').Last();
        var clean = new string(Path.GetFileNameWithoutExtension(leaf).Where(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or ' ').Take(100).ToArray()).Trim();
        return (clean.Length == 0 ? "evidence" : clean) + (contentType == "application/pdf" ? ".pdf" : contentType == "image/png" ? ".png" : ".jpg");
    }
}
