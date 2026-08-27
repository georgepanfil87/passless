using System.Formats.Cbor;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Fido2NetLib;
using Fido2NetLib.Objects;

namespace Passless.IntegrationTests.Registration;

/// <summary>
/// A software stand-in for a passkey authenticator, good enough to produce
/// attestation responses the server will accept.
/// </summary>
/// <remarks>
/// This assembles authenticator data and encodes an attestation object, which
/// is close to the line this project draws around hand-rolling protocol
/// primitives. Two things keep it on the right side of that line: the CBOR
/// encoding is done by <see cref="CborWriter"/> from the base class library
/// rather than by hand, and none of this ships — it exists only so the failure
/// paths can be tested without a browser and a fingerprint. The production code
/// never constructs one of these; it only ever parses them, through Fido2NetLib.
///
/// Attestation format is "none", so there is no attestation statement to sign.
/// </remarks>
internal sealed class SoftwareAuthenticator
{
    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    public byte[] CredentialId { get; init; } = RandomNumberGenerator.GetBytes(32);

    public Guid Aaguid { get; init; } = Guid.Parse("6028b017-b1d4-4c02-b4b3-afcdafc96bb2");

    public bool BackupEligible { get; init; } = true;

    public bool BackupState { get; init; } = true;

    public uint SignCount { get; init; }

    public AuthenticatorAttestationRawResponse Attest(
        CredentialCreateOptions options,
        string origin,
        string? relyingPartyIdOverride = null,
        string ceremonyType = "webauthn.create")
    {
        var clientDataJson = JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object>
        {
            ["type"] = ceremonyType,
            ["challenge"] = Base64Url(options.Challenge),
            ["origin"] = origin,
            ["crossOrigin"] = false,
        });

        var authenticatorData = BuildAuthenticatorData(
            relyingPartyIdOverride ?? options.Rp.Id,
            SignCount,
            includeAttestedCredentialData: true);

        return new AuthenticatorAttestationRawResponse
        {
            Id = Base64Url(CredentialId),
            RawId = CredentialId,
            Type = PublicKeyCredentialType.PublicKey,
            Response = new AuthenticatorAttestationRawResponse.AttestationResponse
            {
                AttestationObject = EncodeAttestationObject(authenticatorData),
                ClientDataJson = clientDataJson,
                Transports = [AuthenticatorTransport.Internal, AuthenticatorTransport.Hybrid],
            },
            ClientExtensionResults = new AuthenticationExtensionsClientOutputs(),
        };
    }

    /// <summary>
    /// Signs an assertion. This is the half that needs a real signature: the
    /// "none" attestation used at registration carries no statement to sign,
    /// but an assertion is a signature over authenticatorData concatenated with
    /// the SHA-256 of clientDataJSON, and the server verifies it against the
    /// stored public key.
    /// </summary>
    /// <param name="signCount">
    /// Set by the caller rather than tracked here, so a test can present a
    /// counter that has gone backwards without the authenticator having to
    /// misbehave.
    /// </param>
    public AuthenticatorAssertionRawResponse Assert(
        AssertionOptions options,
        string origin,
        Guid userHandle,
        uint signCount,
        string? relyingPartyIdOverride = null,
        string ceremonyType = "webauthn.get")
    {
        var clientDataJson = JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object>
        {
            ["type"] = ceremonyType,
            ["challenge"] = Base64Url(options.Challenge),
            ["origin"] = origin,
            ["crossOrigin"] = false,
        });

        var authenticatorData = BuildAuthenticatorData(
            relyingPartyIdOverride ?? options.RpId ?? throw new InvalidOperationException("Assertion options carry no RP ID."),
            signCount,
            includeAttestedCredentialData: false);

        var signedPayload = new byte[authenticatorData.Length + 32];
        authenticatorData.CopyTo(signedPayload, 0);
        SHA256.HashData(clientDataJson).CopyTo(signedPayload, authenticatorData.Length);

        return new AuthenticatorAssertionRawResponse
        {
            Id = Base64Url(CredentialId),
            RawId = CredentialId,
            Type = PublicKeyCredentialType.PublicKey,
            Response = new AuthenticatorAssertionRawResponse.AssertionResponse
            {
                AuthenticatorData = authenticatorData,
                ClientDataJson = clientDataJson,
                // ES256 signatures travel as an ASN.1 DER SEQUENCE, not as the
                // raw r||s pair .NET produces by default. Asking the BCL for the
                // DER form beats assembling the encoding here.
                Signature = _key.SignData(
                    signedPayload,
                    HashAlgorithmName.SHA256,
                    DSASignatureFormat.Rfc3279DerSequence),
                UserHandle = userHandle.ToByteArray(),
            },
            ClientExtensionResults = new AuthenticationExtensionsClientOutputs(),
        };
    }

    private byte[] BuildAuthenticatorData(
        string relyingPartyId,
        uint signCount,
        bool includeAttestedCredentialData)
    {
        const byte UserPresent = 0x01;
        const byte UserVerified = 0x04;
        const byte BackupEligibleFlag = 0x08;
        const byte BackupStateFlag = 0x10;
        const byte AttestedCredentialData = 0x40;

        var flags = (byte)(UserPresent | UserVerified
            | (includeAttestedCredentialData ? AttestedCredentialData : 0)
            | (BackupEligible ? BackupEligibleFlag : 0)
            | (BackupState ? BackupStateFlag : 0));

        using var buffer = new MemoryStream();

        buffer.Write(SHA256.HashData(Encoding.UTF8.GetBytes(relyingPartyId)));
        buffer.WriteByte(flags);

        Span<byte> counter = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(counter, signCount);
        buffer.Write(counter);

        // Assertions carry no attested credential data; the AT flag above is
        // cleared for them and everything below is registration-only.
        if (!includeAttestedCredentialData)
        {
            return buffer.ToArray();
        }

        // Big-endian, per the WebAuthn encoding of the AAGUID. Guid.ToByteArray()
        // without the flag emits the first three groups little-endian, which
        // would round-trip through .NET and be wrong on the wire.
        buffer.Write(Aaguid.ToByteArray(bigEndian: true));

        Span<byte> credentialIdLength = stackalloc byte[2];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(credentialIdLength, (ushort)CredentialId.Length);
        buffer.Write(credentialIdLength);
        buffer.Write(CredentialId);
        buffer.Write(EncodeCoseKey());

        return buffer.ToArray();
    }

    private byte[] EncodeCoseKey()
    {
        var parameters = _key.ExportParameters(includePrivateParameters: false);

        var writer = new CborWriter(CborConformanceMode.Ctap2Canonical);
        writer.WriteStartMap(5);
        writer.WriteInt32(1);                                  // kty
        writer.WriteInt32(2);                                  //   EC2
        writer.WriteInt32(3);                                  // alg
        writer.WriteInt32(-7);                                 //   ES256
        writer.WriteInt32(-1);                                 // crv
        writer.WriteInt32(1);                                  //   P-256
        writer.WriteInt32(-2);
        writer.WriteByteString(parameters.Q.X!);               // x
        writer.WriteInt32(-3);
        writer.WriteByteString(parameters.Q.Y!);               // y
        writer.WriteEndMap();

        return writer.Encode();
    }

    private static byte[] EncodeAttestationObject(byte[] authenticatorData)
    {
        var writer = new CborWriter(CborConformanceMode.Ctap2Canonical);
        writer.WriteStartMap(3);
        writer.WriteTextString("fmt");
        writer.WriteTextString("none");
        writer.WriteTextString("attStmt");
        writer.WriteStartMap(0);
        writer.WriteEndMap();
        writer.WriteTextString("authData");
        writer.WriteByteString(authenticatorData);
        writer.WriteEndMap();

        return writer.Encode();
    }

    private static string Base64Url(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
