using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using ClaimPilot.API.Controllers;
using ClaimPilot.API.Dtos;
using ClaimPilot.Domain.Entities;
using ClaimPilot.Domain.Enums;
using ClaimPilot.Domain.Exceptions;
using ClaimPilot.Infrastructure.Configuration;
using ClaimPilot.Infrastructure.Services;

namespace ClaimPilot.Tests.Integration;

public sealed class ClaimDocumentsEndpointTests
{
    private static Claim BuildClaim() => new()
    {
        Id = Guid.NewGuid(),
        ClaimNumber = "CLAIM-2026-002",
        PolicyNumber = "AUT-2022",
        IncidentDate = new DateTime(2024, 6, 15, 0, 0, 0, DateTimeKind.Utc),
        ClaimAmount = 1400m,
        Description = "My car was hit by another vehicle at the intersection of Main and 5th.",
        Status = ClaimStatus.Submitted,
        CreatedAt = DateTime.UtcNow
    };

    private static FormFile PdfFile(string name = "police_report.pdf")
    {
        var stream = new MemoryStream(new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x34 });
        var file = new FormFile(stream, 0, stream.Length, name, name)
        {
            Headers = new HeaderDictionary()
        };
        file.ContentType = "application/pdf";
        return file;
    }

    private static TController WithHttpContext<TController>(TController controller)
        where TController : ControllerBase
    {
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        return controller;
    }

    [Fact]
    public async Task UploadDocument_ToNonexistentClaim_Returns404()
    {
        var controller = WithHttpContext(new ClaimsController(
            new FakeClaimRepository(), new FakePolicyRepository(), new FakeAuditService(),
            new FakeStorageService(), new NullTraceViewBuilder()));

        var result = await controller.UploadDocument(
            Guid.NewGuid(), new UploadDocumentRequest("PoliceReport", PdfFile()), CancellationToken.None);

        result.Result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task UploadDocument_WithPdf_StoresFileAndMetadata()
    {
        var root = Path.Combine(Path.GetTempPath(), "cp-uploads-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var claim = BuildClaim();
            var claims = new FakeClaimRepository();
            claims.Claims.Add(claim);
            var storage = new FileStorageService(
                Options.Create(new StorageOptions { UploadRoot = root }),
                NullLogger<FileStorageService>.Instance);
            var controller = WithHttpContext(new ClaimsController(
                claims, new FakePolicyRepository(), new FakeAuditService(), storage, new NullTraceViewBuilder()));

            var result = await controller.UploadDocument(
                claim.Id, new UploadDocumentRequest("PoliceReport", PdfFile()), CancellationToken.None);

            var created = result.Result.Should().BeOfType<CreatedResult>().Subject;
            created.StatusCode.Should().Be(201);
            created.Location.Should().StartWith($"/api/claims/{claim.Id}/documents/");

            var dto = created.Value.Should().BeOfType<ClaimDocumentDto>().Subject;
            dto.ClaimId.Should().Be(claim.Id);
            dto.FileName.Should().Be("police_report.pdf");
            dto.ContentType.Should().Be("application/pdf");
            dto.SizeBytes.Should().Be(8);

            claims.Documents.Should().ContainSingle(d => d.Id == dto.Id);
            var storedDir = Path.Combine(root, claim.Id.ToString("N"));
            Directory.Exists(storedDir).Should().BeTrue();
            var storedFiles = Directory.GetFiles(storedDir);
            storedFiles.Should().NotBeEmpty();
            storedFiles.Should().NotContain(p => Path.GetFileName(p) == "police_report.pdf",
                "the client-supplied name must never be used on disk");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task UploadDocument_WithDisallowedExtension_Returns400()
    {
        var claim = BuildClaim();
        var claims = new FakeClaimRepository();
        claims.Claims.Add(claim);
        var controller = WithHttpContext(new ClaimsController(
            claims, new FakePolicyRepository(), new FakeAuditService(), new FakeStorageService(), new NullTraceViewBuilder()));

        // .exe is outside the allow-list; the client-declared Content-Type is ignored,
        // so validation must reject on the extension regardless of declared type.
        var fakeFile = new FormFile(new MemoryStream(new byte[] { 1, 2, 3, 4 }), 0, 4, "virus.exe", "virus.exe")
        {
            Headers = new HeaderDictionary(),
            ContentType = "application/x-msdownload"
        };

        await Assert.ThrowsAsync<ValidationException>(() =>
            controller.UploadDocument(claim.Id, new UploadDocumentRequest("Other", fakeFile), CancellationToken.None));
    }

    [Fact]
    public async Task UploadDocument_WithMismatchedMagicBytes_Returns400()
    {
        var claim = BuildClaim();
        var claims = new FakeClaimRepository();
        claims.Claims.Add(claim);
        var controller = WithHttpContext(new ClaimsController(
            claims, new FakePolicyRepository(), new FakeAuditService(), new FakeStorageService(), new NullTraceViewBuilder()));

        // .pdf extension but the payload is not a PDF (no %PDF header).
        var fakeFile = new FormFile(new MemoryStream(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }), 0, 8, "f.pdf", "f.pdf")
        {
            Headers = new HeaderDictionary(),
            ContentType = "application/pdf"
        };

        await Assert.ThrowsAsync<ValidationException>(() =>
            controller.UploadDocument(claim.Id, new UploadDocumentRequest("PoliceReport", fakeFile), CancellationToken.None));
    }
}
