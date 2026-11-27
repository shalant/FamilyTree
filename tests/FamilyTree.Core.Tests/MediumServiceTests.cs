using FamilyTree.Core.Models;
using FamilyTree.Core.Services;
using FamilyTree.Core.Tests.Helpers;
using FamilyTree.Shared.DTOs.Medium;
using Microsoft.Extensions.Logging.Abstractions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Xunit;

namespace FamilyTree.Core.Tests;

public class MediumServiceTests
{
    private static byte[] MakeJpeg(int width, int height)
    {
        using var image = new Image<Rgba32>(width, height, Color.CornflowerBlue.ToPixel<Rgba32>());
        using var ms = new MemoryStream();
        image.SaveAsJpeg(ms);
        return ms.ToArray();
    }

    private static async Task<(MediumService Service, FakeBlobStorageService Blob, Guid PersonId)> CreateSutAsync()
    {
        var dbFactory = new TestDbContextFactory();
        await using (var ctx = dbFactory.CreateDbContext())
        {
            // Caller and person share a family: since 2026-10-10 uploads are scoped to the
            // caller's family (finding S4), so a family-less caller can't upload at all.
            var familyId = Guid.NewGuid();
            var person = new Person { Id = Guid.NewGuid(), FirstName = "Test", LastName = "Person", FamilyId = familyId };
            ctx.People.Add(person);
            await ctx.SaveChangesAsync();

            var blob = new FakeBlobStorageService();
            var service = new MediumService(
                dbFactory, blob, NullLogger<MediumService>.Instance,
                new FakeAuditLogService(), new FakeCurrentUserService { UserId = Guid.NewGuid(), FamilyId = familyId });
            return (service, blob, person.Id);
        }
    }

    // A Member can't call PersonService.UpdateAsync, so deleting the profile photo has to
    // clear it on the person here (2026-11-26).
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DeleteAsync_ClearsTheProfilePhotoOnlyIfItWasThatPhoto(bool deletingTheProfilePhoto)
    {
        var dbFactory = new TestDbContextFactory();
        var familyId = Guid.NewGuid();
        var personId = Guid.NewGuid();
        var mediumId = Guid.NewGuid();
        var uploader = Guid.NewGuid();
        const string url = "https://fake-blob/a.jpg";
        var profileUrl = deletingTheProfilePhoto ? url : "https://fake-blob/other.jpg";
        await using (var ctx = dbFactory.CreateDbContext())
        {
            ctx.People.Add(new Person
            {
                Id = personId, FirstName = "Test", LastName = "Person", FamilyId = familyId,
                ProfilePhotoUrl = profileUrl,
            });
            ctx.Media.Add(new Medium
            {
                Id = mediumId, PersonId = personId, Url = url, FileName = "a.jpg",
                MimeType = "image/jpeg", Type = "photo", CreatedBy = uploader,
            });
            await ctx.SaveChangesAsync();
        }
        var service = new MediumService(
            dbFactory, new FakeBlobStorageService(), NullLogger<MediumService>.Instance,
            new FakeAuditLogService(), new FakeCurrentUserService { UserId = uploader, FamilyId = familyId });

        var result = await service.DeleteAsync(mediumId);

        Assert.True(result.Success, result.Message);
        await using var check = dbFactory.CreateDbContext();
        Assert.Equal(deletingTheProfilePhoto ? null : profileUrl, check.People.Single().ProfilePhotoUrl);
    }

    // Decided 2026-11-26: a Member deletes only what they added; admins delete anything.
    // A row with no recorded uploader counts as someone else's.
    [Theory]
    [InlineData("uploader", true, true)]
    [InlineData("other member", true, false)]
    [InlineData("other member", false, false)]
    [InlineData("admin", true, true)]
    [InlineData("super-user", true, true)]
    public async Task DeleteAsync_IsForTheUploaderAndAdmins(string who, bool uploaderRecorded, bool allowed)
    {
        var dbFactory = new TestDbContextFactory();
        var familyId = Guid.NewGuid();
        var personId = Guid.NewGuid();
        var mediumId = Guid.NewGuid();
        var uploader = Guid.NewGuid();
        await using (var ctx = dbFactory.CreateDbContext())
        {
            ctx.People.Add(new Person { Id = personId, FirstName = "Test", LastName = "Person", FamilyId = familyId });
            ctx.Media.Add(new Medium
            {
                Id = mediumId, PersonId = personId, Url = "https://fake-blob/a.jpg", FileName = "a.jpg",
                MimeType = "image/jpeg", Type = "photo", CreatedBy = uploaderRecorded ? uploader : null,
            });
            await ctx.SaveChangesAsync();
        }
        var user = new FakeCurrentUserService
        {
            UserId = who == "uploader" ? uploader : Guid.NewGuid(),
            FamilyId = familyId,
            IsAdmin = who == "admin",
            IsSuperUser = who == "super-user",
        };
        var service = new MediumService(
            dbFactory, new FakeBlobStorageService(), NullLogger<MediumService>.Instance,
            new FakeAuditLogService(), user);

        var listed = await service.GetByPersonIdAsync(personId);
        var result = await service.DeleteAsync(mediumId);

        Assert.Equal(allowed, Assert.Single(listed.Data!).CanDelete);
        Assert.Equal(allowed, result.Success);
        await using var check = dbFactory.CreateDbContext();
        Assert.Equal(allowed ? 0 : 1, check.Media.Count());
    }

    [Fact]
    public async Task CreateAsync_SmallImage_UploadsUnchanged()
    {
        var (service, blob, personId) = await CreateSutAsync();
        var bytes = MakeJpeg(400, 300);

        var dto = new MediumUpsertDto
        {
            PersonId = personId, FileName = "small.jpg", MimeType = "image/jpeg", Type = "photo",
        };
        var result = await service.CreateAsync(dto, new MemoryStream(bytes));

        Assert.True(result.Success);
        Assert.NotNull(blob.LastUploadedBytes);
        using var uploaded = await Image.LoadAsync(new MemoryStream(blob.LastUploadedBytes!));
        Assert.Equal(400, uploaded.Width);
        Assert.Equal(300, uploaded.Height);
    }

    [Fact]
    public async Task CreateAsync_OversizedImage_IsResizedBeforeUpload()
    {
        var (service, blob, personId) = await CreateSutAsync();
        var bytes = MakeJpeg(4000, 2500); // real phone-camera-ish dimensions

        var dto = new MediumUpsertDto
        {
            PersonId = personId, FileName = "big.jpg", MimeType = "image/jpeg", Type = "photo",
        };
        var result = await service.CreateAsync(dto, new MemoryStream(bytes));

        Assert.True(result.Success);
        Assert.NotNull(blob.LastUploadedBytes);
        using var uploaded = await Image.LoadAsync(new MemoryStream(blob.LastUploadedBytes!));
        Assert.True(uploaded.Width <= 2000);
        Assert.True(uploaded.Height <= 2000);
        // Aspect ratio preserved (4000x2500 -> 2000x1250)
        Assert.Equal(2000, uploaded.Width);
        Assert.Equal(1250, uploaded.Height);
    }

    [Fact]
    public async Task CreateAsync_UndecodableBytesWithImageMimeType_FallsBackToOriginalBytes_DoesNotThrow()
    {
        var (service, blob, personId) = await CreateSutAsync();
        var garbage = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };

        var dto = new MediumUpsertDto
        {
            PersonId = personId, FileName = "notreally.jpg", MimeType = "image/jpeg", Type = "photo",
        };
        var result = await service.CreateAsync(dto, new MemoryStream(garbage));

        Assert.True(result.Success);
        Assert.Equal(garbage, blob.LastUploadedBytes);
    }

    private static byte[] MakeImage(string format)
    {
        using var image = new Image<Rgba32>(10, 10, Color.CornflowerBlue.ToPixel<Rgba32>());
        using var ms = new MemoryStream();
        switch (format)
        {
            case "tiff": image.SaveAsTiff(ms); break;
            case "bmp": image.SaveAsBmp(ms); break;
            case "png": image.SaveAsPng(ms); break;
            case "webp": image.SaveAsWebp(ms); break;
            case "gif": image.SaveAsGif(ms); break;
            default: throw new ArgumentException(format);
        }
        return ms.ToArray();
    }

    // The MIME allow-list trusts the browser's label, so a TIFF sent as image/jpeg used to be
    // decoded by ImageSharp's TIFF decoder (open advisories in 3.1.x, docs/security.md A03).
    [Theory]
    [InlineData("tiff")]
    [InlineData("bmp")]
    public async Task CreateAsync_DisallowedFormatLabelledAsJpeg_IsRejectedAndNotUploaded(string format)
    {
        var (service, blob, personId) = await CreateSutAsync();

        var dto = new MediumUpsertDto
        {
            PersonId = personId, FileName = "photo.jpg", MimeType = "image/jpeg", Type = "photo",
        };
        var result = await service.CreateAsync(dto, new MemoryStream(MakeImage(format)));

        Assert.False(result.Success);
        Assert.Contains("JPEG, PNG, WebP or GIF", result.Message);
        Assert.Null(blob.LastUploadedBytes);
    }

    [Theory]
    [InlineData("png", "image/png")]
    [InlineData("webp", "image/webp")]
    [InlineData("gif", "image/gif")]
    public async Task CreateAsync_AllowedFormat_Uploads(string format, string mimeType)
    {
        var (service, blob, personId) = await CreateSutAsync();
        var bytes = MakeImage(format);

        var dto = new MediumUpsertDto
        {
            PersonId = personId, FileName = $"photo.{format}", MimeType = mimeType, Type = "photo",
        };
        var result = await service.CreateAsync(dto, new MemoryStream(bytes));

        Assert.True(result.Success);
        Assert.Equal(bytes, blob.LastUploadedBytes);
    }
}
