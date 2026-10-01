using Marketplace.Api.Verification;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Marketplace.Api.Catalog;

public sealed class ServiceImage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ServiceId { get; set; }
    public string AltText { get; set; } = "";
    public string ObjectKey { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public int Width { get; set; }
    public int Height { get; set; }
    public long Size { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DeletedAt { get; set; }
}
public sealed class ServiceImageDeletion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ObjectKey { get; set; } = "";
    public DateTime DueAt { get; set; } = DateTime.UtcNow;
    public int Attempts { get; set; }
}
public static class ServiceImagePolicy
{
    public const int MaxInputBytes = 5 * 1024 * 1024;
    public const int MaxOutputBytes = 8 * 1024 * 1024;
    public const int MaxRequestBytes = 6 * 1024 * 1024;
    public const int MaxImages = 5;
    public const int MaxDimension = 4096;
    public const long MaxPixels = 12000000;
    public const int OutputDimension = 1600;
}
public interface IServiceImageStore
{
    Task Put(string key, byte[] bytes, CancellationToken ct);
    Task<byte[]> Get(string key, CancellationToken ct);
    Task Delete(string key, CancellationToken ct);
}
public sealed class S3ServiceImageStore : IServiceImageStore
{
    private readonly S3EvidenceStore storage;
    public S3ServiceImageStore(IConfiguration config, IWebHostEnvironment environment)
    {
        var bucket = config["ServiceImages:Bucket"] ?? "service-images";
        if (string.IsNullOrWhiteSpace(bucket) || bucket == (config["Storage:Bucket"] ?? "verification-evidence"))
            throw new InvalidOperationException("Service images require a separate private bucket.");
        var access = config["ServiceImages:AccessKey"] ?? (environment.IsDevelopment() ? config["Storage:AccessKey"] : null);
        var secret = config["ServiceImages:SecretKey"] ?? (environment.IsDevelopment() ? config["Storage:SecretKey"] : null);
        if (string.IsNullOrWhiteSpace(access) || string.IsNullOrWhiteSpace(secret))
            throw new InvalidOperationException("Configure dedicated service-image storage credentials outside Development.");
        var scoped = new ConfigurationBuilder().AddConfiguration(config)
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Storage:Bucket"] = bucket,
                ["Storage:AccessKey"] = access, ["Storage:SecretKey"] = secret,
                ["Storage:Endpoint"] = config["ServiceImages:Endpoint"] ?? config["Storage:Endpoint"] }).Build();
        storage = new S3EvidenceStore(scoped, environment);
    }
    private static string ValidateKey(string key)
    {
        if (key.Length != 46 || !key.StartsWith("processed/", StringComparison.Ordinal) || !key.EndsWith(".png", StringComparison.Ordinal)
            || !Guid.TryParseExact(key.AsSpan(10, 32), "N", out _)) throw new EvidenceUnavailableException();
        return key;
    }
    public Task Put(string key, byte[] bytes, CancellationToken ct) => storage.Put(ValidateKey(key), bytes, ct);
    public Task<byte[]> Get(string key, CancellationToken ct) => storage.Get(ValidateKey(key), ct);
    public Task Delete(string key, CancellationToken ct) => storage.Delete(ValidateKey(key), ct);
}
public sealed class InvalidServiceImageException : Exception { }
public sealed class ImageCapacityException : Exception { }
public sealed record ProcessedServiceImage(byte[] Bytes, int Width, int Height);
public sealed class ServiceImageProcessor
{
    private readonly SemaphoreSlim gate = new(2, 2);
    private readonly Configuration configuration;
    public ServiceImageProcessor()
    {
        configuration = new Configuration(new JpegConfigurationModule(), new PngConfigurationModule())
        {
            MaxDegreeOfParallelism = 2,
            MemoryAllocator = MemoryAllocator.Create(new MemoryAllocatorOptions { AllocationLimitMegabytes = 128, MaximumPoolSizeMegabytes = 32 })
        };
    }
    public async Task<ProcessedServiceImage> Process(byte[] bytes, string mime, CancellationToken ct)
    {
        if (!await gate.WaitAsync(0, ct)) throw new ImageCapacityException();
        try
        {
            if (bytes.Length == 0 || bytes.Length > ServiceImagePolicy.MaxInputBytes || mime is not ("image/jpeg" or "image/png") || EvidenceFileValidation.Detect(bytes) != mime)
                throw new InvalidServiceImageException();
            if (mime == "image/png") RejectAnimationChunks(bytes);
            var options = new DecoderOptions { Configuration = configuration, SkipMetadata = true, MaxFrames = 2 };
            using var source = new MemoryStream(bytes, false);
            var info = await Image.IdentifyAsync(options, source, ct);
            if (info.Width < 1 || info.Height < 1 || info.Width > ServiceImagePolicy.MaxDimension || info.Height > ServiceImagePolicy.MaxDimension
                || (long)info.Width * info.Height > ServiceImagePolicy.MaxPixels || info.FrameMetadataCollection.Count > 1)
                throw new InvalidServiceImageException();
            source.Position = 0;
            using var image = await Image.LoadAsync<Rgba32>(options, source, ct);
            if (image.Frames.Count != 1) throw new InvalidServiceImageException();
            if (image.Width > ServiceImagePolicy.OutputDimension || image.Height > ServiceImagePolicy.OutputDimension)
                image.Mutate(x => x.Resize(new ResizeOptions { Mode = ResizeMode.Max, Size = new Size(ServiceImagePolicy.OutputDimension, ServiceImagePolicy.OutputDimension) }));
            // Copy pixels into a fresh image: no EXIF/GPS, ICC, XMP, IPTC or PNG text is retained.
            using var clean = new Image<Rgba32>(configuration, image.Width, image.Height);
            image.ProcessPixelRows(clean, (from, to) => { for (var y = 0; y < from.Height; y++) from.GetRowSpan(y).CopyTo(to.GetRowSpan(y)); });
            using var output = new MemoryStream();
            await clean.SaveAsync(output, new PngEncoder { SkipMetadata = true }, ct);
            if (output.Length > ServiceImagePolicy.MaxOutputBytes) throw new InvalidServiceImageException();
            return new ProcessedServiceImage(output.ToArray(), clean.Width, clean.Height);
        }
        catch (Exception e) when (e is UnknownImageFormatException or InvalidImageContentException or InvalidMemoryOperationException or ArgumentException)
        { throw new InvalidServiceImageException(); }
        finally { gate.Release(); }
    }
    private static void RejectAnimationChunks(byte[] bytes)
    {
        for (var offset = 8; offset < bytes.Length;)
        {
            if (offset + 12 > bytes.Length) throw new InvalidServiceImageException();
            var length = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset, 4));
            if ((long)offset + 12 + length > bytes.Length) throw new InvalidServiceImageException();
            var type = bytes.AsSpan(offset + 4, 4);
            if (type.SequenceEqual("acTL"u8) || type.SequenceEqual("fcTL"u8) || type.SequenceEqual("fdAT"u8))
                throw new InvalidServiceImageException();
            offset += checked((int)length + 12);
        }
    }
}
