using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Marketplace.Api.Catalog;
using Marketplace.Api.Verification;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using Xunit;

namespace Marketplace.Api.Tests;

public sealed partial class VerificationTests
{
    private static byte[] ServicePhoto(bool jpeg = false, int width = 80, int height = 40, bool animated = false)
    {
        using var image = new Image<Rgba32>(width, height, new Rgba32(40, 100, 180));
        image.Metadata.ExifProfile = new ExifProfile(); image.Metadata.ExifProfile.SetValue(ExifTag.Artist, "PRIVATE METADATA GPS MARKER");
        if (animated) image.Frames.AddFrame(image.Frames.RootFrame);
        using var output = new MemoryStream(); if (jpeg) image.SaveAsJpeg(output); else image.SaveAsPng(output); return output.ToArray();
    }
    private static MultipartFormDataContent PhotoForm(byte[]? bytes = null, string mime = "image/png", string alt = "Fictional cleaning equipment", bool acknowledged = true)
    {
        var form = new MultipartFormDataContent(); var file = new ByteArrayContent(bytes ?? ServicePhoto());
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(mime);
        form.Add(file, "file", "../../PRIVATE.jpg"); form.Add(new StringContent(alt), "altText");
        if (acknowledged) form.Add(new StringContent("true"), "demoAcknowledged"); return form;
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ImageProcessingReencodesPixelsAndStripsMetadata(bool jpeg)
    {
        var processor = new ServiceImageProcessor(); var input = ServicePhoto(jpeg, 2000, 1000);
        var output = await processor.Process(input, jpeg ? "image/jpeg" : "image/png", CancellationToken.None);
        Assert.Equal(1600, output.Width); Assert.Equal(800, output.Height);
        Assert.Equal("image/png", EvidenceFileValidation.Detect(output.Bytes));
        using var image = Image.Load(output.Bytes);
        Assert.Null(image.Metadata.ExifProfile); Assert.Null(image.Metadata.IccProfile); Assert.Null(image.Metadata.XmpProfile); Assert.Null(image.Metadata.IptcProfile);
        Assert.DoesNotContain("PRIVATE METADATA", System.Text.Encoding.UTF8.GetString(output.Bytes));
    }
    [Fact]
    public async Task ProcessorRejectsMimeSpoofingBrokenImagesExcessiveDimensionsAndAnimation()
    {
        var processor = new ServiceImageProcessor();
        var input = ServicePhoto();
        await Assert.ThrowsAsync<InvalidServiceImageException>(() => processor.Process(input, "image/jpeg", CancellationToken.None));
        await Assert.ThrowsAsync<InvalidServiceImageException>(() => processor.Process(input, "image/svg+xml", CancellationToken.None));
        await Assert.ThrowsAsync<InvalidServiceImageException>(() => processor.Process(input[..^5], "image/png", CancellationToken.None));
        await Assert.ThrowsAsync<InvalidServiceImageException>(() => processor.Process(ServicePhoto(width: 4097, height: 1), "image/png", CancellationToken.None));
        await Assert.ThrowsAsync<InvalidServiceImageException>(() => processor.Process(ServicePhoto(width: 3500, height: 3500), "image/png", CancellationToken.None));
        await Assert.ThrowsAsync<InvalidServiceImageException>(() => processor.Process(ServicePhoto(animated: true), "image/png", CancellationToken.None));
    }
    [Fact]
    public async Task ServiceImagesStaySeparatePrivateUntilEligiblePublicationAndUseSafeHeaders()
    {
        await using var factory = new Factory(); using var ownerClient = factory.Browser(); using var reviewerClient = factory.Browser(); using var anonymous = factory.Browser();
        var owner = new Browser(ownerClient); var reviewer = new Browser(reviewerClient);
        var (org, _, _) = await ApprovedPublicSetup(factory, owner, reviewer); await Publish(owner, org);
        var (service, etag) = await CreateOffer(owner, org); var raw = ServicePhoto(jpeg: true);
        var uploaded = await owner.Send($"organizations/{org}/services/{service}/images", body: PhotoForm(raw, "image/jpeg"), etag: etag);
        Assert.Equal(HttpStatusCode.OK, uploaded.StatusCode); var listing = await uploaded.Content.ReadFromJsonAsync<JsonElement>(); var image = listing.GetProperty("images")[0]; var id = image.GetProperty("id").GetGuid();
        Assert.Equal("Draft", listing.GetProperty("status").GetString());
        Assert.Equal(new[] {"altText", "contentType", "createdAt", "height", "id", "size", "url", "width"}, image.EnumerateObject().Select(p => p.Name).Order().ToArray());
        Assert.DoesNotContain("PRIVATE.jpg", image.ToString()); Assert.DoesNotContain("objectKey", image.ToString()); Assert.DoesNotContain("sha256", image.ToString());
        Assert.Single(factory.ImageStorage.Objects); Assert.Single(factory.Storage.Objects); // Only the private review evidence is in evidence storage.
        var stored = factory.ImageStorage.Objects.Single().Value; Assert.False(raw.SequenceEqual(stored));
        var privateUrl = image.GetProperty("url").GetString()!; var publicUrl = $"/api/v1/services/{service}/images/{id}/content";
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(privateUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ownerClient.GetAsync(privateUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync(publicUrl)).StatusCode);
        var published = await OfferPublication(owner, org, service, uploaded.Headers.ETag!.ToString()); Assert.Equal(HttpStatusCode.OK, published.StatusCode);
        var content = await anonymous.GetAsync(publicUrl); Assert.Equal(HttpStatusCode.OK, content.StatusCode);
        Assert.Equal("image/png", content.Content.Headers.ContentType!.MediaType); Assert.Equal("no-store", content.Headers.CacheControl!.ToString());
        Assert.Equal("nosniff", content.Headers.GetValues("X-Content-Type-Options").Single()); Assert.Contains("sandbox", content.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Equal(stored, await content.Content.ReadAsByteArrayAsync());
        var publicListing = await anonymous.GetFromJsonAsync<JsonElement>($"/api/v1/services/{service}");
        Assert.Equal(publicUrl, publicListing.GetProperty("images")[0].GetProperty("url").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/v1/services/{Guid.NewGuid()}/images/{id}/content")).StatusCode);
        await factory.Update(async db => (await db.VerificationChecks.SingleAsync()).ExpiresAt = DateTime.UtcNow.AddSeconds(-1));
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync(publicUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ownerClient.GetAsync(privateUrl)).StatusCode);
    }
    [Fact]
    public async Task ImageUploadsAndRemovalRequireOwnerCsrfExactVersionAndValidMultipart()
    {
        await using var factory = new Factory(); using var ownerClient = factory.Browser(); using var staffClient = factory.Browser();
        var owner = new Browser(ownerClient); var staff = new Browser(staffClient); var (org, _, _) = await Setup(factory, owner); var (service, etag) = await CreateOffer(owner, org);
        var root = $"organizations/{org}/services/{service}/images";
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.Send(root, body: PhotoForm(), etag: etag, csrf: false)).StatusCode);
        Assert.Equal((HttpStatusCode)428, (await owner.Send(root, body: PhotoForm())).StatusCode);
        Assert.Equal(HttpStatusCode.PreconditionFailed, (await owner.Send(root, body: PhotoForm(), etag: "*")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.Send(root, body: PhotoForm(acknowledged: false), etag: etag)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.Send(root, body: PhotoForm(alt: ""), etag: etag)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.Send(root, body: PhotoForm(bytes: Pdf, mime: "application/pdf"), etag: etag)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.Send(root, body: PhotoForm(bytes: ServicePhoto(), mime: "image/jpeg"), etag: etag)).StatusCode);
        using var doubleFile = PhotoForm(); doubleFile.Add(new ByteArrayContent(ServicePhoto()), "file", "second.png");
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.Send(root, body: doubleFile, etag: etag)).StatusCode);
        Assert.Equal((HttpStatusCode)413, (await owner.Send(root, body: PhotoForm(new byte[ServiceImagePolicy.MaxInputBytes + 1]), etag: etag)).StatusCode);
        await staff.Login("staff@example.test");
        Assert.Equal(HttpStatusCode.NotFound, (await staff.Send(root, body: PhotoForm(), etag: etag)).StatusCode);
        await factory.Update(async db => db.OrganizationMemberships.Add(new Marketplace.Api.Organizations.OrganizationMembership {OrganizationId = org, UserId = (await db.Users.SingleAsync(u => u.Email == "staff@example.test")).Id, Role = "Staff", Active = true}));
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.Send(root, body: PhotoForm(), etag: etag)).StatusCode);
        var uploaded = await owner.Send(root, body: PhotoForm(), etag: etag); Assert.Equal(HttpStatusCode.OK, uploaded.StatusCode);
        var image = (await uploaded.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("images")[0]; var url = image.GetProperty("url").GetString()!;
        Assert.Equal(HttpStatusCode.OK, (await staffClient.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.Send(root + "/" + image.GetProperty("id").GetGuid(), HttpMethod.Delete, etag: uploaded.Headers.ETag!.ToString())).StatusCode);
        await factory.Update(async db => (await db.OrganizationMemberships.SingleAsync(m => m.Role == "Staff")).Active = false);
        Assert.Equal(HttpStatusCode.NotFound, (await staffClient.GetAsync(url)).StatusCode);
    }
    [Fact]
    public async Task ImageRemovalAndArchiveDemoteOrHideListingsAndQueueSeparateDeletion()
    {
        await using var factory = new Factory(); using var ownerClient = factory.Browser(); using var reviewerClient = factory.Browser(); using var anonymous = factory.Browser();
        var owner = new Browser(ownerClient); var reviewer = new Browser(reviewerClient); var (org, _, _) = await ApprovedPublicSetup(factory, owner, reviewer); await Publish(owner, org);
        var (service, etag) = await CreateOffer(owner, org); var root = $"organizations/{org}/services/{service}";
        var uploaded = await owner.Send(root + "/images", body: PhotoForm(), etag: etag); var imageId = (await uploaded.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("images")[0].GetProperty("id").GetGuid();
        var published = await OfferPublication(owner, org, service, uploaded.Headers.ETag!.ToString());
        var removed = await owner.Send(root + "/images/" + imageId, HttpMethod.Delete, etag: published.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.OK, removed.StatusCode); var listing = await removed.Content.ReadFromJsonAsync<JsonElement>(); Assert.Equal("Draft", listing.GetProperty("status").GetString()); Assert.Equal(0, listing.GetProperty("images").GetArrayLength());
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/v1/services/{service}/images/{imageId}/content")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ownerClient.GetAsync("/api/v1/" + root + "/images/" + imageId + "/content")).StatusCode);
        var second = await owner.Send(root + "/images", body: PhotoForm(), etag: removed.Headers.ETag!.ToString());
        var archived = await owner.Send(root + "/archive", etag: second.Headers.ETag!.ToString()); Assert.Equal(HttpStatusCode.OK, archived.StatusCode);
        Assert.Equal(0, (await archived.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("images").GetArrayLength());
        Assert.Equal(HttpStatusCode.Conflict, (await owner.Send(root + "/images", body: PhotoForm(), etag: archived.Headers.ETag!.ToString())).StatusCode);
        var worker = new ServiceImageCleanupWorker(factory.Services.GetRequiredService<IServiceScopeFactory>(), Microsoft.Extensions.Logging.Abstractions.NullLogger<ServiceImageCleanupWorker>.Instance);
        await worker.Sweep(CancellationToken.None); Assert.Empty(factory.ImageStorage.Objects); Assert.Single(factory.Storage.Objects);
        await factory.Update(async db => Assert.Equal(0, await db.ServiceImageDeletions.CountAsync()));
    }
    [Fact]
    public async Task ScannerAndStorageFailuresFailClosedAndStaleUploadsLeaveCleanupReservations()
    {
        await using var factory = new Factory(); using var client = factory.Browser(); var owner = new Browser(client); var (org, _, _) = await Setup(factory, owner); var (service, etag) = await CreateOffer(owner, org);
        var route = $"organizations/{org}/services/{service}/images";
        factory.Scan.Fail = true; Assert.Equal(HttpStatusCode.ServiceUnavailable, (await owner.Send(route, body: PhotoForm(), etag: etag)).StatusCode); factory.Scan.Fail = false;
        factory.Scan.Clean = false; Assert.Equal(HttpStatusCode.BadRequest, (await owner.Send(route, body: PhotoForm(), etag: etag)).StatusCode); factory.Scan.Clean = true;
        factory.ImageStorage.Fail = true; Assert.Equal(HttpStatusCode.ServiceUnavailable, (await owner.Send(route, body: PhotoForm(), etag: etag)).StatusCode); factory.ImageStorage.Fail = false;
        factory.ImageStorage.OnPut = () => factory.Update(async db => (await db.ServiceListings.SingleAsync()).Version = Guid.NewGuid());
        Assert.Equal(HttpStatusCode.PreconditionFailed, (await owner.Send(route, body: PhotoForm(), etag: etag)).StatusCode);
        factory.ImageStorage.OnPut = null;
        await factory.Update(async db => {Assert.Equal(0, await db.ServiceImages.CountAsync()); Assert.Equal(2, await db.ServiceImageDeletions.CountAsync()); foreach(var job in await db.ServiceImageDeletions.ToListAsync()) job.DueAt = DateTime.UtcNow.AddSeconds(-1);});
        var worker = new ServiceImageCleanupWorker(factory.Services.GetRequiredService<IServiceScopeFactory>(), Microsoft.Extensions.Logging.Abstractions.NullLogger<ServiceImageCleanupWorker>.Instance);
        factory.ImageStorage.Fail = true; await worker.Sweep(CancellationToken.None);
        await factory.Update(async db => {Assert.All(await db.ServiceImageDeletions.ToListAsync(), job => Assert.Equal(1, job.Attempts)); foreach(var job in await db.ServiceImageDeletions.ToListAsync()) job.DueAt = DateTime.UtcNow.AddSeconds(-1);});
        factory.ImageStorage.Fail = false; await worker.Sweep(CancellationToken.None); Assert.Empty(factory.ImageStorage.Objects);
    }
    [Fact]
    public async Task OwnerRevocationDuringUploadPreventsMetadataCommitAndKeepsOrphanReservation()
    {
        await using var factory = new Factory(); using var client = factory.Browser(); var owner = new Browser(client);
        var (org, _, _) = await Setup(factory, owner); var (service, etag) = await CreateOffer(owner, org);
        factory.ImageStorage.OnPut = () => factory.Update(async db => (await db.OrganizationMemberships.SingleAsync(m => m.OrganizationId == org)).Active = false);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.Send($"organizations/{org}/services/{service}/images", body: PhotoForm(), etag: etag)).StatusCode);
        await factory.Update(async db => {Assert.Equal(0, await db.ServiceImages.CountAsync()); Assert.Equal(1, await db.ServiceImageDeletions.CountAsync());});
        Assert.Single(factory.ImageStorage.Objects);
    }
    [Fact]
    public async Task PublicEligibilityIsRecheckedAfterStorageRetrieval()
    {
        await using var factory = new Factory(); using var ownerClient = factory.Browser(); using var reviewerClient = factory.Browser(); using var anonymous = factory.Browser();
        var owner = new Browser(ownerClient); var reviewer = new Browser(reviewerClient);
        var (org, _, _) = await ApprovedPublicSetup(factory, owner, reviewer); await Publish(owner, org); var (service, etag) = await CreateOffer(owner, org);
        var uploaded = await owner.Send($"organizations/{org}/services/{service}/images", body: PhotoForm(), etag: etag);
        var imageId = (await uploaded.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("images")[0].GetProperty("id").GetGuid();
        await OfferPublication(owner, org, service, uploaded.Headers.ETag!.ToString());
        factory.ImageStorage.OnGet = () => factory.Update(async db => (await db.VerificationChecks.SingleAsync()).RevokedAt = DateTime.UtcNow);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/v1/services/{service}/images/{imageId}/content")).StatusCode);
    }
    [Fact]
    public async Task ImageCountLimitAndStoredIntegrityAreEnforced()
    {
        await using var factory = new Factory(); using var client = factory.Browser(); var owner = new Browser(client); var (org, _, _) = await Setup(factory, owner); var (service, etag) = await CreateOffer(owner, org);
        var route = $"organizations/{org}/services/{service}/images"; string? url = null;
        for(var i = 0; i < 5; i++)
        {
            var uploaded = await owner.Send(route, body: PhotoForm(), etag: etag); Assert.Equal(HttpStatusCode.OK, uploaded.StatusCode); etag = uploaded.Headers.ETag!.ToString();
            url = (await uploaded.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("images")[0].GetProperty("url").GetString();
        }
        Assert.Equal(HttpStatusCode.Conflict, (await owner.Send(route, body: PhotoForm(), etag: etag)).StatusCode);
        var key = factory.ImageStorage.Objects.Keys.First(); factory.ImageStorage.Objects[key] = [1, 2, 3];
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync(url)).StatusCode);
        Assert.Equal(5, factory.ImageStorage.Objects.Count);
    }
}
