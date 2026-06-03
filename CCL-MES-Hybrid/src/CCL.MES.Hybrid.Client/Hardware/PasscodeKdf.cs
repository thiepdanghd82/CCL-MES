using System.Security.Cryptography;
using System.Text;

namespace CCL.MES.Hybrid.Client.Hardware;

/// <summary>
/// P10.3 W4 — kiosk passcode key-derivation. PBKDF2-HMAC-SHA256 with a
/// per-device salt (the immutable device id) + a generated 16-byte
/// random salt mixed in via the encoded blob. 200_000 iterations is the
/// 2024 OWASP minimum for PBKDF2-SHA256 (we err on the high side; a
/// kiosk verifies a passcode only when the operator unlocks /mode, so
/// the ~50-100 ms cost is invisible).
///
/// Free-license — entirely from the .NET BCL
/// (<see cref="Rfc2898DeriveBytes"/>). No NuGet package needed.
///
/// Encoded form: <c>"pbkdf2$v1$iterations$base64salt$base64hash"</c>.
/// The version segment lets a future swap to Argon2id / scrypt /
/// higher iteration counts ship without breaking existing stored
/// hashes — <see cref="Verify"/> rejects unknown versions.
///
/// Why both a random salt AND the device id?
/// Random salt ensures two identical passcodes on the same station
/// produce different hashes (no rainbow table). Device id ensures a
/// passcode set on station A can't be replayed against station B even
/// if an attacker exfiltrates the salt+hash blob. Both are stored;
/// the device id is implicit (in <c>InMemoryDeviceModeService.DeviceId</c>).
/// </summary>
public static class PasscodeKdf
{
    private const string Algorithm = "pbkdf2";
    private const string Version = "v1";
    private const int Iterations = 200_000;
    private const int SaltLengthBytes = 16;
    private const int HashLengthBytes = 32;

    /// <summary>
    /// Hash <paramref name="passcode"/> for storage. Returns the encoded
    /// "pbkdf2$v1$iter$salt$hash" blob. Empty or whitespace passcodes
    /// throw — callers MUST gate on non-empty input before calling.
    /// </summary>
    public static string Hash(string passcode, string deviceIdSalt)
    {
        if (string.IsNullOrWhiteSpace(passcode))
            throw new ArgumentException("Passcode is empty.", nameof(passcode));
        if (string.IsNullOrWhiteSpace(deviceIdSalt))
            throw new ArgumentException("Device id salt is empty.", nameof(deviceIdSalt));

        var salt = RandomNumberGenerator.GetBytes(SaltLengthBytes);
        var derived = Derive(passcode, deviceIdSalt, salt, Iterations);
        return Encode(Iterations, salt, derived);
    }

    /// <summary>
    /// Constant-time verify of <paramref name="candidate"/> against the
    /// encoded <paramref name="storedHash"/>. Returns false on any parse
    /// failure or unknown version (defensive — never throws to the
    /// caller; verification is a hot path that must degrade safely).
    /// </summary>
    public static bool Verify(string candidate, string deviceIdSalt, string storedHash)
    {
        if (string.IsNullOrEmpty(candidate)) return false;
        if (string.IsNullOrEmpty(storedHash)) return false;

        if (!TryDecode(storedHash, out var iter, out var salt, out var expected))
            return false;

        var derived = Derive(candidate, deviceIdSalt, salt, iter);
        return CryptographicOperations.FixedTimeEquals(derived, expected);
    }

    /// <summary>True when the encoded blob is a well-formed
    /// pbkdf2$v1$... — useful for migration checks ("does the stored
    /// hash need upgrading from SHA-256 to PBKDF2?").</summary>
    public static bool LooksLikePbkdf2(string storedHash) =>
        !string.IsNullOrEmpty(storedHash) && storedHash.StartsWith($"{Algorithm}${Version}$", StringComparison.Ordinal);

    private static byte[] Derive(string passcode, string deviceIdSalt, byte[] randomSalt, int iterations)
    {
        // Combine device-id-salt + random-salt: HMAC the device id into the
        // input password BEFORE PBKDF2 so the device id participates in
        // the derived key without bloating the salt-storage shape.
        var keyedInput = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(deviceIdSalt),
            Encoding.UTF8.GetBytes(passcode));
        return Rfc2898DeriveBytes.Pbkdf2(
            password: keyedInput,
            salt: randomSalt,
            iterations: iterations,
            hashAlgorithm: HashAlgorithmName.SHA256,
            outputLength: HashLengthBytes);
    }

    private static string Encode(int iterations, byte[] salt, byte[] derived)
    {
        return string.Concat(
            Algorithm, "$", Version, "$",
            iterations.ToString(System.Globalization.CultureInfo.InvariantCulture), "$",
            Convert.ToBase64String(salt), "$",
            Convert.ToBase64String(derived));
    }

    private static bool TryDecode(string encoded, out int iterations, out byte[] salt, out byte[] hash)
    {
        iterations = 0;
        salt = Array.Empty<byte>();
        hash = Array.Empty<byte>();

        var parts = encoded.Split('$');
        if (parts.Length != 5) return false;
        if (parts[0] != Algorithm) return false;
        if (parts[1] != Version) return false;
        if (!int.TryParse(parts[2], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out iterations))
            return false;
        if (iterations < 10_000 || iterations > 5_000_000) return false; // sanity bounds

        try
        {
            salt = Convert.FromBase64String(parts[3]);
            hash = Convert.FromBase64String(parts[4]);
        }
        catch (FormatException)
        {
            return false;
        }

        return salt.Length is >= 8 and <= 64 && hash.Length is 32;
    }
}
