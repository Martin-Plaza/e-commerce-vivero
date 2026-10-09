using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using GymShop.Infrastructure.Configuration;

namespace GymShop.Tests.Configuration;

public sealed class ArcaOptionsTests
{
    [Fact]
    public void Disabled_connector_does_not_require_secrets()
    {
        Assert.Empty(new ArcaOptions().Validate(enabled: false));
    }

    [Fact]
    public void Enabled_connector_requires_homologation_secrets()
    {
        var failures = new ArcaOptions().Validate(enabled: true);

        Assert.Contains(failures, value => value.Contains("CertificatePemBase64", StringComparison.Ordinal));
        Assert.Contains(failures, value => value.Contains("PrivateKeyPemBase64", StringComparison.Ordinal));
    }

    [Fact]
    public void Production_is_explicitly_blocked()
    {
        var options = new ArcaOptions
        {
            Environment = "Production",
            CertificatePemBase64 = "not-a-certificate",
            PrivateKeyPemBase64 = "not-a-key"
        };

        Assert.Contains(options.Validate(enabled: true), value => value.Contains("Production is intentionally blocked", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100000)]
    public void Enabled_connector_rejects_invalid_homologation_point_of_sale(int pointOfSale)
    {
        var options = new ArcaOptions
        {
            HomologationPointOfSale = pointOfSale,
            CertificatePemBase64 = "configured",
            PrivateKeyPemBase64 = "configured"
        };

        Assert.Contains(options.Validate(enabled: true), value =>
            value.Contains("HomologationPointOfSale", StringComparison.Ordinal));
    }

    [Fact]
    public void Load_signing_material_returns_a_matching_key_that_can_sign_cms()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=GymShop ARCA Homologation", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var source = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
        var options = new ArcaOptions
        {
            CertificatePemBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(source.ExportCertificatePem())),
            PrivateKeyPemBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(rsa.ExportPkcs8PrivateKeyPem()))
        };

        var signingMaterial = options.LoadSigningMaterial();
        using var certificate = signingMaterial.Certificate;
        using var privateKey = signingMaterial.PrivateKey;
        var signedCms = new SignedCms(new ContentInfo("probe"u8.ToArray()));

        signedCms.ComputeSignature(new CmsSigner(certificate) { PrivateKey = privateKey }, silent: true);

        Assert.False(certificate.HasPrivateKey);
        Assert.NotEmpty(signedCms.Encode());
    }
}
