using CCL.MES.Hybrid.Client.Hardware;

namespace CCL.MES.Hybrid.Client.Tests.Hardware;

/// <summary>
/// P10.3 W4 — PBKDF2 round-trip + tamper guards + version sanity.
/// </summary>
public sealed class PasscodeKdfTests
{
    private const string Device1 = "0193a1d9-aaaa-1111-aaaa-aaaaaaaaaaaa";
    private const string Device2 = "0193a1d9-bbbb-2222-bbbb-bbbbbbbbbbbb";

    [Fact]
    public void Hash_then_Verify_roundtrips()
    {
        var encoded = PasscodeKdf.Hash("Passcode42!", Device1);
        Assert.True(PasscodeKdf.Verify("Passcode42!", Device1, encoded));
    }

    [Fact]
    public void Verify_wrong_passcode_returns_false()
    {
        var encoded = PasscodeKdf.Hash("right", Device1);
        Assert.False(PasscodeKdf.Verify("wrong", Device1, encoded));
    }

    [Fact]
    public void Verify_wrong_device_id_returns_false()
    {
        // Same passcode hashed on Device1 must NOT verify against Device2.
        // This is the per-device pinning guarantee.
        var encoded = PasscodeKdf.Hash("same-pass", Device1);
        Assert.False(PasscodeKdf.Verify("same-pass", Device2, encoded));
    }

    [Fact]
    public void Hashing_same_passcode_twice_produces_different_blobs()
    {
        // Random salt guarantee — two hashes of the same passcode on the
        // same device must NEVER collide.
        var a = PasscodeKdf.Hash("dup", Device1);
        var b = PasscodeKdf.Hash("dup", Device1);
        Assert.NotEqual(a, b);
        // But both verify.
        Assert.True(PasscodeKdf.Verify("dup", Device1, a));
        Assert.True(PasscodeKdf.Verify("dup", Device1, b));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Hash_rejects_empty_passcode(string raw)
    {
        Assert.Throws<ArgumentException>(() => PasscodeKdf.Hash(raw, Device1));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Hash_rejects_empty_device_id(string id)
    {
        Assert.Throws<ArgumentException>(() => PasscodeKdf.Hash("x", id));
    }

    [Fact]
    public void Verify_returns_false_on_empty_inputs()
    {
        // Defensive — verification is a hot path and must NEVER throw.
        Assert.False(PasscodeKdf.Verify("", Device1, PasscodeKdf.Hash("x", Device1)));
        Assert.False(PasscodeKdf.Verify("x", Device1, ""));
    }

    [Theory]
    [InlineData("plain-text-hex-sha256-1234567890abcdef")]
    [InlineData("sha256$v1$abc$def$ghi")]
    [InlineData("pbkdf2$v2$200000$abc$def")] // unsupported version
    [InlineData("pbkdf2$v1$abc$def$ghi")]    // non-integer iterations
    [InlineData("pbkdf2$v1$200000$!!!$ghi")] // bad base64
    [InlineData("pbkdf2$v1$200000$YWJj")]    // missing hash segment
    public void Verify_rejects_unknown_or_malformed_blobs(string blob)
    {
        Assert.False(PasscodeKdf.Verify("x", Device1, blob));
    }

    [Fact]
    public void Encoded_blob_starts_with_pbkdf2_v1_marker()
    {
        var encoded = PasscodeKdf.Hash("any", Device1);
        Assert.StartsWith("pbkdf2$v1$", encoded, StringComparison.Ordinal);
        Assert.True(PasscodeKdf.LooksLikePbkdf2(encoded));
    }

    [Fact]
    public void LooksLikePbkdf2_rejects_legacy_sha256_hex()
    {
        var hex = "ABCDEF0123456789".PadRight(64, '0');
        Assert.False(PasscodeKdf.LooksLikePbkdf2(hex));
    }
}
