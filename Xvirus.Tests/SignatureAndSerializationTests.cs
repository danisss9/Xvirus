using System;
using System.IO;
using System.Text.Json;
using BaseLibrary.Serializers;
using Xunit;
using Xvirus.Model;

namespace Xvirus.Tests;

public class SignatureAndSerializationTests
{
    private static readonly string Notepad = Path.Combine(Environment.SystemDirectory, "notepad.exe");

    [Fact]
    public void SignatureVerifier_TrustsCatalogSignedWindowsFile()
    {
        if (!OperatingSystem.IsWindows()) return;

        // notepad.exe has no embedded signature; it is signed through a Windows security catalog.
        Assert.Null(Utils.GetCertificateSubjectName(Notepad));
        Assert.True(SignatureVerifier.IsTrusted(Notepad, hasEmbeddedSignature: false, checkCatalog: true));
        Assert.False(SignatureVerifier.IsTrusted(Notepad, hasEmbeddedSignature: false, checkCatalog: false));
    }

    [Fact]
    public void SignatureVerifier_TrustsValidEmbeddedSignature()
    {
        if (!OperatingSystem.IsWindows()) return;

        var signed = FindEmbeddedSignedFile();
        Assert.True(SignatureVerifier.IsTrusted(signed, hasEmbeddedSignature: true, checkCatalog: false));
    }

    [Fact]
    public void SignatureVerifier_RejectsTamperedSignedFile()
    {
        if (!OperatingSystem.IsWindows()) return;

        var tampered = Path.Combine(Path.GetTempPath(), $"xv_tampered_{Guid.NewGuid():N}.exe");
        try
        {
            // Change one byte of code but keep the signature block: the certificate still parses,
            // yet the signature no longer matches the file.
            var bytes = File.ReadAllBytes(FindEmbeddedSignedFile());
            bytes[0x400] ^= 0xFF;
            File.WriteAllBytes(tampered, bytes);

            Assert.NotNull(Utils.GetCertificateSubjectName(tampered));
            Assert.False(SignatureVerifier.IsTrusted(tampered, hasEmbeddedSignature: true, checkCatalog: true));
        }
        finally
        {
            File.Delete(tampered);
        }
    }

    [Fact]
    public void SignatureVerifier_RejectsUnsignedExecutable()
    {
        if (!OperatingSystem.IsWindows()) return;

        var unsigned = Path.Combine(Path.GetTempPath(), $"xv_unsigned_{Guid.NewGuid():N}.exe");
        try
        {
            var bytes = new byte[4096];
            new Random(1).NextBytes(bytes);
            bytes[0] = (byte)'M';
            bytes[1] = (byte)'Z';
            File.WriteAllBytes(unsigned, bytes);

            Assert.Null(Utils.GetCertificateSubjectName(unsigned));
            Assert.False(SignatureVerifier.IsTrusted(unsigned, hasEmbeddedSignature: false, checkCatalog: true));
        }
        finally
        {
            File.Delete(unsigned);
        }
    }

    [Fact]
    public void ScanResultArray_SerializesAsJsonArray()
    {
        var results = new[]
        {
            new ScanResult(0, "Safe", @"C:\data\report [final].docx"),
            new ScanResult(1, "Trojan.Test", @"C:\data\a]b[c.exe"),
        };

        var json = JsonSerializer.Serialize(results, SourceGenerationContext.Default.ScanResultArray);
        using var doc = JsonDocument.Parse(json);

        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
        Assert.Equal(2, doc.RootElement.GetArrayLength());
        Assert.Equal(@"C:\data\report [final].docx", doc.RootElement[0].GetProperty("Path").GetString());
        Assert.True(doc.RootElement[1].GetProperty("IsMalware").GetBoolean());
    }

    /// <summary>The test host itself (dotnet / testhost) ships with an embedded Microsoft signature.</summary>
    private static string FindEmbeddedSignedFile()
    {
        foreach (var candidate in new[] { Environment.ProcessPath, Path.Combine(AppContext.BaseDirectory, "testhost.exe") })
        {
            if (candidate != null && File.Exists(candidate) && Utils.GetCertificateSubjectName(candidate) != null)
                return candidate;
        }
        throw new InvalidOperationException("No file with an embedded Authenticode signature was found for the test.");
    }
}
