using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;

namespace Marshal.Api.Tests.Auth;

/// <summary>
/// A WebAuthn platform authenticator with one ES256 credential: answers creation options with a <c>none</c>
/// attestation and request options with a signed assertion, serialized like <c>PublicKeyCredential.toJSON()</c>.
/// </summary>
internal sealed class FakeAuthenticator(string origin, string rpId) : IDisposable
{
    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly byte[] _credentialId = RandomNumberGenerator.GetBytes(16);
    private string? _userHandle;
    private uint _signCount;

    public string Create(string creationOptionsJson)
    {
        using var options = JsonDocument.Parse(creationOptionsJson);
        _userHandle = options.RootElement.GetProperty("user").GetProperty("id").GetString();
        string challenge = options.RootElement.GetProperty("challenge").GetString()!;
        byte[] clientData = ClientData("webauthn.create", challenge);

        ECParameters parameters = _key.ExportParameters(false);
        byte[] coseKey = Cbor.Map(
            (Cbor.Int(1), Cbor.Int(2)),
            (Cbor.Int(3), Cbor.Int(-7)),
            (Cbor.Int(-1), Cbor.Int(1)),
            (Cbor.Int(-2), Cbor.Bytes(parameters.Q.X!)),
            (Cbor.Int(-3), Cbor.Bytes(parameters.Q.Y!)));
        byte[] credentialLength = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(credentialLength, (ushort)_credentialId.Length);
        byte[] authData = [.. AuthDataHeader(flags: 0x45), .. new byte[16], .. credentialLength, .. _credentialId, .. coseKey];
        byte[] attestationObject = Cbor.Map(
            (Cbor.Text("fmt"), Cbor.Text("none")),
            (Cbor.Text("attStmt"), Cbor.Map()),
            (Cbor.Text("authData"), Cbor.Bytes(authData)));

        return JsonSerializer.Serialize(new
        {
            id = Base64UrlEncoder.Encode(_credentialId),
            rawId = Base64UrlEncoder.Encode(_credentialId),
            type = "public-key",
            authenticatorAttachment = "platform",
            clientExtensionResults = new { },
            response = new
            {
                clientDataJSON = Base64UrlEncoder.Encode(clientData),
                attestationObject = Base64UrlEncoder.Encode(attestationObject),
                transports = new[] { "internal" },
            },
        });
    }

    public string Get(string requestOptionsJson)
    {
        using var options = JsonDocument.Parse(requestOptionsJson);
        string challenge = options.RootElement.GetProperty("challenge").GetString()!;
        byte[] clientData = ClientData("webauthn.get", challenge);
        _signCount++;
        byte[] authData = AuthDataHeader(flags: 0x05);
        byte[] signature = _key.SignData([.. authData, .. SHA256.HashData(clientData)], HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);

        return JsonSerializer.Serialize(new
        {
            id = Base64UrlEncoder.Encode(_credentialId),
            rawId = Base64UrlEncoder.Encode(_credentialId),
            type = "public-key",
            authenticatorAttachment = "platform",
            clientExtensionResults = new { },
            response = new
            {
                clientDataJSON = Base64UrlEncoder.Encode(clientData),
                authenticatorData = Base64UrlEncoder.Encode(authData),
                signature = Base64UrlEncoder.Encode(signature),
                userHandle = _userHandle,
            },
        });
    }

    public void Dispose() => _key.Dispose();

    private byte[] ClientData(string type, string challenge) =>
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { type, challenge, origin, crossOrigin = false }));

    private byte[] AuthDataHeader(byte flags)
    {
        byte[] counter = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(counter, _signCount);
        return [.. SHA256.HashData(Encoding.UTF8.GetBytes(rpId)), flags, .. counter];
    }

    /// <summary>The few CBOR items WebAuthn attestation needs.</summary>
    private static class Cbor
    {
        public static byte[] Int(long value) => value >= 0 ? Head(0, (ulong)value) : Head(1, (ulong)(-1 - value));

        public static byte[] Bytes(byte[] value) => [.. Head(2, (ulong)value.Length), .. value];

        public static byte[] Text(string value)
        {
            byte[] utf8 = Encoding.UTF8.GetBytes(value);
            return [.. Head(3, (ulong)utf8.Length), .. utf8];
        }

        public static byte[] Map(params (byte[] Key, byte[] Value)[] entries) =>
            [.. Head(5, (ulong)entries.Length), .. entries.SelectMany(entry => entry.Key.Concat(entry.Value))];

        private static byte[] Head(int major, ulong value)
        {
            byte type = (byte)(major << 5);
            return value switch
            {
                < 24 => [(byte)(type | (byte)value)],
                <= byte.MaxValue => [(byte)(type | 24), (byte)value],
                <= ushort.MaxValue => [(byte)(type | 25), (byte)(value >> 8), (byte)value],
                _ => [(byte)(type | 26), (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value],
            };
        }
    }
}
