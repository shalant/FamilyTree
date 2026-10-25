using FamilyTree.Core.Mappers;
using FamilyTree.Core.Models;
using FamilyTree.Core.Services;
using FamilyTree.Core.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FamilyTree.Core.Tests;

// S8 (docs/security.md, 2026-10-25): media were shown from public blob URLs, readable by
// anyone with the link, even after the person was deleted. They're now served through
// /media/{blob} to the person's own family, and /media/invite/{token} for a story invite.
public class MediaPathsTests
{
    [Theory]
    [InlineData("https://arborkin.blob.core.windows.net/media/photo-0b5e.jpg", "/media/photo-0b5e.jpg")]
    [InlineData("http://127.0.0.1:10000/devstoreaccount1/media/photo-1.png", "/media/photo-1.png")]
    [InlineData("/media/photo-1.png", "/media/photo-1.png")]
    public void BlobUrls_BecomeAppPaths(string stored, string expected) =>
        MediaPaths.ToAppPath(stored).Should().Be(expected);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("https://example.com/somebody.jpg")]
    [InlineData("https://fake-blob/b1.jpg")]
    public void AnythingElse_IsLeftAlone(string? stored) =>
        MediaPaths.ToAppPath(stored).Should().Be(stored);

    [Theory]
    [InlineData("/media/..%2Fsecret")]
    [InlineData("/media/a%2Fb.jpg")]
    [InlineData("/media/.hidden")]
    [InlineData("/media/")]
    [InlineData("https://arborkin.blob.core.windows.net/media/a%20b.jpg")]
    public void UnsafeNames_AreNotBlobNames(string url) =>
        MediaPaths.BlobName(url).Should().BeNull();

    [Fact]
    public void PersonDto_CarriesTheAppPath()
    {
        var person = new Person
        {
            Id = Guid.NewGuid(), FirstName = "A", LastName = "B",
            ProfilePhotoUrl = "https://arborkin.blob.core.windows.net/media/photo-1.jpg",
        };

        PersonMapper.MapPersonToDto(person, []).ProfilePhotoUrl.Should().Be("/media/photo-1.jpg");
    }
}

public class MediaAccessServiceTests
{
    private const string Blob = "https://arborkin.blob.core.windows.net/media/";
    private readonly TestDbContextFactory _db = new();
    private readonly Guid _family = Guid.NewGuid();

    private MediaAccessService Service(Guid? familyId = null) => new(
        _db, new FakeBlobStorageService(), NullLogger<MediaAccessService>.Instance,
        new FakeCurrentUserService { UserId = Guid.NewGuid(), FamilyId = familyId ?? _family });

    private async Task<Person> AddPersonAsync(string? photo = null, bool deleted = false)
    {
        await using var ctx = _db.CreateDbContext();
        var p = new Person
        {
            Id = Guid.NewGuid(), FirstName = "Ada", LastName = "Lane", FamilyId = _family,
            ProfilePhotoUrl = photo, CreatedAt = DateTime.UtcNow, DeletedAt = deleted ? DateTime.UtcNow : null,
        };
        ctx.People.Add(p);
        await ctx.SaveChangesAsync();
        return p;
    }

    private async Task AddMediumAsync(Guid personId, string name, string? mime)
    {
        await using var ctx = _db.CreateDbContext();
        ctx.Media.Add(new Medium
        {
            Id = Guid.NewGuid(), PersonId = personId, Url = Blob + name, FileName = name,
            MimeType = mime, Type = "photo", CreatedAt = DateTime.UtcNow,
        });
        await ctx.SaveChangesAsync();
    }

    private async Task<string> AddInviteAsync(Guid personId, DateTime expires)
    {
        await using var ctx = _db.CreateDbContext();
        var token = "tok-" + Guid.NewGuid().ToString("N");
        ctx.StoryInvites.Add(new StoryInvite
        {
            Id = Guid.NewGuid(), Token = token, PersonId = personId, InvitedEmail = "x@x.test",
            InvitedByUserId = Guid.NewGuid(), CreatedAt = DateTime.UtcNow, ExpiresAt = expires,
        });
        await ctx.SaveChangesAsync();
        return token;
    }

    [Fact]
    public async Task OwnFamilysPhoto_IsServedAsAnImage()
    {
        var p = await AddPersonAsync();
        await AddMediumAsync(p.Id, "photo-1.jpg", "image/jpeg");

        var r = await Service().OpenAsync("photo-1.jpg");

        r.Success.Should().BeTrue(r.Message);
        r.Data!.ContentType.Should().Be("image/jpeg");
        r.Data.Inline.Should().BeTrue();
    }

    [Fact]
    public async Task ProfilePhotoWithNoMediaRow_IsServed()
    {
        await AddPersonAsync(photo: Blob + "photo-2.png");

        (await Service().OpenAsync("photo-2.png")).Data!.ContentType.Should().Be("image/png");
    }

    [Fact]
    public async Task DeletedPersonsPhoto_IsNotServed()
    {
        var p = await AddPersonAsync(photo: Blob + "photo-3.jpg", deleted: true);
        await AddMediumAsync(p.Id, "photo-3.jpg", "image/jpeg");

        (await Service().OpenAsync("photo-3.jpg")).Success.Should().BeFalse();
    }

    [Fact]
    public async Task AnotherFamily_AndNoFamily_GetNotFound()
    {
        var p = await AddPersonAsync();
        await AddMediumAsync(p.Id, "photo-4.jpg", "image/jpeg");

        (await Service(Guid.NewGuid()).OpenAsync("photo-4.jpg")).Success.Should().BeFalse();
        var noFamily = new MediaAccessService(_db, new FakeBlobStorageService(),
            NullLogger<MediaAccessService>.Instance, new FakeCurrentUserService());
        (await noFamily.OpenAsync("photo-4.jpg")).Success.Should().BeFalse();
    }

    // A file uploaded with no MIME type skips MediumService's allow-list (S9), so its bytes
    // could be HTML. It must never be served as something a browser renders.
    [Fact]
    public async Task NonImage_IsServedAsADownload()
    {
        var p = await AddPersonAsync();
        await AddMediumAsync(p.Id, "photo-5.html", null);

        var r = await Service().OpenAsync("photo-5.html");

        r.Data!.ContentType.Should().Be("application/octet-stream");
        r.Data.Inline.Should().BeFalse();
    }

    [Fact]
    public async Task InviteToken_UnlocksTheSubjectsPhotoUntilItExpires()
    {
        var p = await AddPersonAsync(photo: Blob + "photo-6.webp");
        var live = await AddInviteAsync(p.Id, DateTime.UtcNow.AddDays(1));
        var expired = await AddInviteAsync(p.Id, DateTime.UtcNow.AddDays(-1));

        (await Service().OpenInvitePhotoAsync(live)).Data!.ContentType.Should().Be("image/webp");
        (await Service().OpenInvitePhotoAsync(expired)).Success.Should().BeFalse();
        (await Service().OpenInvitePhotoAsync("no-such-token")).Success.Should().BeFalse();
    }

    [Fact]
    public void InvitePhotoUrl_PointsAtTheTokenRoute()
    {
        StoryInviteService.InvitePhotoUrl(Blob + "photo-7.jpg", "tok", "https://arborkin.com/")
            .Should().Be("https://arborkin.com/media/invite/tok");
        StoryInviteService.InvitePhotoUrl(Blob + "photo-7.jpg", "tok").Should().Be("/media/invite/tok");
        StoryInviteService.InvitePhotoUrl(null, "tok").Should().BeNull();
    }
}
