using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using OnlineShop.Application.Common;
using OnlineShop.Application.Customers;
using OnlineShop.Application.Notifications;

namespace OnlineShop.Infrastructure.Notifications;

/// <summary>
/// Section <c>WebPush</c>. Web Push is on only with a VAPID key pair (generate one with
/// <c>dotnet run --project src/OnlineShop.Api -- generate-vapid-keys</c>); the private key is a secret
/// (user-secrets / environment variables).
/// </summary>
public sealed class WebPushOptions
{
    public const string SectionName = "WebPush";

    /// <summary>The VAPID public key: base64url, 65 bytes (uncompressed P-256 point).</summary>
    public string? PublicKey { get; set; }

    /// <summary>The VAPID private key: base64url, 32 bytes.</summary>
    public string? PrivateKey { get; set; }

    /// <summary>Who push services may contact about this shop ("mailto:…" or an https address); default the shop's address.</summary>
    public string? Subject { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(PublicKey) && !string.IsNullOrWhiteSpace(PrivateKey);
}

/// <summary>The message encryption of RFC 8291 ("aes128gcm" content coding, RFC 8188), one record.</summary>
public static class WebPushCrypto
{
    private const int RecordSize = 4096;

    /// <summary>Encrypts for a browser with a new key pair and salt (what <see cref="WebPushSender"/> does).</summary>
    public static byte[] Encrypt(byte[] plaintext, byte[] userAgentPublicKey, byte[] authSecret)
    {
        using var serverKey = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        return Encrypt(plaintext, userAgentPublicKey, authSecret, serverKey, RandomNumberGenerator.GetBytes(16));
    }

    /// <summary>The encryption with a given key pair and salt (deterministic: for the RFC's test vector).</summary>
    public static byte[] Encrypt(byte[] plaintext, byte[] userAgentPublicKey, byte[] authSecret, ECDiffieHellman serverKey, byte[] salt)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        ArgumentNullException.ThrowIfNull(userAgentPublicKey);
        ArgumentNullException.ThrowIfNull(serverKey);
        ArgumentNullException.ThrowIfNull(salt);
        if (plaintext.Length > RecordSize - 17 - 86)
            throw new ArgumentException("The message is too long for one record.", nameof(plaintext));

        using var userAgentKey = ECDiffieHellman.Create(PublicParameters(userAgentPublicKey));
        var serverPublic = PublicKeyBytes(serverKey.ExportParameters(false));
        var ecdhSecret = serverKey.DeriveRawSecretAgreement(userAgentKey.PublicKey);

        // IKM = HKDF(auth secret, ECDH secret, "WebPush: info" || 0 || ua public || as public, 32)
        var keyInfo = Concat(Encoding.ASCII.GetBytes("WebPush: info\0"), userAgentPublicKey, serverPublic);
        var ikm = HKDF.DeriveKey(HashAlgorithmName.SHA256, ecdhSecret, 32, authSecret, keyInfo);
        var prk = HKDF.Extract(HashAlgorithmName.SHA256, ikm, salt);
        var contentKey = HKDF.Expand(HashAlgorithmName.SHA256, prk, 16, Encoding.ASCII.GetBytes("Content-Encoding: aes128gcm\0"));
        var nonce = HKDF.Expand(HashAlgorithmName.SHA256, prk, 12, Encoding.ASCII.GetBytes("Content-Encoding: nonce\0"));

        // One record: the text, then the 0x02 delimiter of the last record.
        var padded = new byte[plaintext.Length + 1];
        plaintext.CopyTo(padded, 0);
        padded[^1] = 2;
        var ciphertext = new byte[padded.Length];
        var tag = new byte[16];
        using (var aes = new AesGcm(contentKey, 16))
            aes.Encrypt(nonce, padded, ciphertext, tag);

        // Header: salt (16) || record size (4, big endian) || key id length (1) || key id (the server's public key).
        var header = new byte[16 + 4 + 1];
        salt.CopyTo(header, 0);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(16), RecordSize);
        header[20] = (byte)serverPublic.Length;
        return Concat(header, serverPublic, ciphertext, tag);
    }

    internal static ECParameters PublicParameters(byte[] uncompressedPoint)
    {
        if (uncompressedPoint.Length != 65 || uncompressedPoint[0] != 4)
            throw new ArgumentException("Not an uncompressed P-256 public key.", nameof(uncompressedPoint));
        return new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = uncompressedPoint[1..33], Y = uncompressedPoint[33..65] },
        };
    }

    internal static byte[] PublicKeyBytes(ECParameters parameters) => Concat([4], parameters.Q.X!, parameters.Q.Y!);

    private static byte[] Concat(params byte[][] parts)
    {
        var result = new byte[parts.Sum(p => p.Length)];
        var offset = 0;
        foreach (var part in parts)
        {
            part.CopyTo(result, offset);
            offset += part.Length;
        }
        return result;
    }
}

/// <summary>VAPID (RFC 8292): the shop's key pair and the signed token that identifies it to push services.</summary>
public static class Vapid
{
    /// <summary>A new key pair as (public, private), base64url, for <see cref="WebPushOptions"/>.</summary>
    public static (string PublicKey, string PrivateKey) GenerateKeys()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var parameters = key.ExportParameters(true);
        return (Base64Url.Encode(WebPushCrypto.PublicKeyBytes(parameters)), Base64Url.Encode(parameters.D));
    }

    /// <summary>The ES256 JWT for one push service (its origin), valid for 12 hours.</summary>
    public static string Token(string audience, string subject, DateTimeOffset now, string publicKey, string privateKey)
    {
        var parameters = WebPushCrypto.PublicParameters(Base64Url.Decode(publicKey));
        parameters.D = Base64Url.Decode(privateKey);
        using var key = ECDsa.Create(parameters);
        var header = Base64Url.Encode("""{"typ":"JWT","alg":"ES256"}"""u8);
        var claims = Base64Url.Encode(JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object>
        {
            ["aud"] = audience,
            ["exp"] = now.AddHours(12).ToUnixTimeSeconds(),
            ["sub"] = subject,
        }));
        var signed = Encoding.ASCII.GetBytes($"{header}.{claims}");
        var signature = key.SignData(signed, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return $"{header}.{claims}.{Base64Url.Encode(signature)}";
    }
}

/// <summary>
/// Sends Web Push messages: the JSON message encrypted for the browser, POSTed to its push service with a VAPID
/// signature. 404/410 mean the browser withdrew the subscription.
/// </summary>
public sealed class WebPushSender(HttpClient http, IOptions<WebPushOptions> options, IOptions<StoreOptions> store, TimeProvider clock) : IWebPushSender
{
    /// <summary>How long a push service keeps an undelivered message (a day).</summary>
    public const int TimeToLiveSeconds = 86_400;

    public bool IsEnabled => options.Value.IsConfigured;

    public string? PublicKey => IsEnabled ? options.Value.PublicKey : null;

    public async Task<PushResult> SendAsync(PushTarget target, PushMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(message);
        var settings = options.Value;
        if (!settings.IsConfigured)
            return PushResult.Failed;

        var endpoint = new Uri(target.Endpoint);
        var payload = JsonSerializer.SerializeToUtf8Bytes(new { title = message.Title, body = message.Body, url = message.Url });
        var body = WebPushCrypto.Encrypt(payload, Base64Url.Decode(target.P256dh), Base64Url.Decode(target.Auth));
        var subject = string.IsNullOrWhiteSpace(settings.Subject) ? store.Value.PublicUrl : settings.Subject;
        var token = Vapid.Token(endpoint.GetLeftPart(UriPartial.Authority), subject, clock.GetUtcNow(), settings.PublicKey!, settings.PrivateKey!);

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = new ByteArrayContent(body) };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        request.Content.Headers.ContentEncoding.Add("aes128gcm");
        request.Headers.TryAddWithoutValidation("TTL", TimeToLiveSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
        request.Headers.TryAddWithoutValidation("Urgency", "normal");
        request.Headers.TryAddWithoutValidation("Authorization", $"vapid t={token}, k={settings.PublicKey}");

        using var response = await http.SendAsync(request, cancellationToken);
        return response.StatusCode switch
        {
            HttpStatusCode.NotFound or HttpStatusCode.Gone => PushResult.Gone,
            _ when response.IsSuccessStatusCode => PushResult.Sent,
            _ => PushResult.Failed,
        };
    }
}
