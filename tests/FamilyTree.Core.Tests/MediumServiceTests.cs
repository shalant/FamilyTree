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
            var person = new Person { Id = Guid.NewGuid(), FirstName = "Test", LastName = "Person" };
            ctx.People.Add(person);
            await ctx.SaveChangesAsync();

            var blob = new FakeBlobStorageService();
            var service = new MediumService(
                dbFactory, blob, NullLogger<MediumService>.Instance,
                new FakeAuditLogService(), new FakeCurrentUserService());
            return (service, blob, person.Id);
        }
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
}
