using System;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace MudServer
{
    /// <summary>
    /// A self-signed certificate for TLS-wrapped telnet (ROADMAP's "TLS-wrapped telnet"
    /// item - encryption without inventing an SSH server, closing the cleartext-password
    /// gap on its own). Self-signed rather than CA-issued: the goal here is stopping
    /// passive eavesdropping on an internal network, not proving server identity to a
    /// public CA chain, and a telnet client doing opportunistic TLS won't validate the
    /// chain the way a browser does anyway. Generated once and persisted alongside the
    /// rest of the game's data so it survives restarts/redeploys against the same
    /// volume, rather than becoming a new identity every time the process starts.
    /// </summary>
    public static class TlsCertificate
    {
        const string FileName = "telnet-tls.pfx";
        // At-rest protection only, not a real secret boundary - this file lives inside
        // the same persistent data volume as everything else the server already trusts.
        const string Password = "winspod-telnet-tls";

        public static X509Certificate2 LoadOrCreate()
        {
            string path = Path.Combine(Server.userFilePath, FileName);

            if (File.Exists(path))
            {
                try
                {
                    return new X509Certificate2(path, Password, X509KeyStorageFlags.Exportable);
                }
                catch (Exception e)
                {
                    Connection.logError("Existing TLS certificate at " + path + " could not be loaded, generating a new one: " + e, "TLS");
                }
            }

            X509Certificate2 cert = Generate();
            Directory.CreateDirectory(Server.userFilePath);
            File.WriteAllBytes(path, cert.Export(X509ContentType.Pfx, Password));
            return cert;
        }

        static X509Certificate2 Generate()
        {
            using RSA rsa = RSA.Create(2048);
            CertificateRequest request = new CertificateRequest(
                "CN=" + AppSettings.Default.TalkerName,
                rsa,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);

            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, false));

            X509Certificate2 selfSigned = request.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(10));

            // CreateSelfSigned's result can't always be used directly with SslStream or
            // re-exported later on every platform - round-tripping through a PFX export/
            // import immediately gives back a certificate guaranteed to support both.
            byte[] pfxBytes = selfSigned.Export(X509ContentType.Pfx, Password);
            return new X509Certificate2(pfxBytes, Password, X509KeyStorageFlags.Exportable);
        }
    }
}
