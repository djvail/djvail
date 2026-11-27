using System.Security.Cryptography;
using System.Text;

namespace VailITDashboard.Data;

/// <summary>
/// Per-monitor API keys. A key looks like "vmk_" + 32 random bytes (Base64Url). Only its SHA-256 hash is
/// stored, so the key is shown once when it is created and can't be read back later.
/// </summary>
/// <remarks>
/// A key created without a machine is bound to the machineId of the first report that uses it.
/// After that it only works for that machine. A machine may have several active keys (handy for rotation).
/// </remarks>
public sealed class ApiKeyStore(Database db)
{
    public const string Prefix = "vmk_";

    public sealed record KeyCheck(bool Valid, long KeyId, string? BoundMachineId);

    /// <summary>Creates a key and returns it in plain text. This is the only time it is available.</summary>
    public string Create(string label, string? machineId = null)
    {
        string key = Prefix + Base64Url(RandomNumberGenerator.GetBytes(32));
        using var c = db.Open();
        using var cmd = c.Command("""
            INSERT INTO api_keys (key_hash, key_prefix, machine_id, label, created) VALUES ($h, $p, $m, $l, $now)
            """).With("$h", Hash(key)).With("$p", key[..10]).With("$m", machineId).With("$l", label ?? "").With("$now", UnixTime.Now);
        cmd.ExecuteNonQuery();
        return key;
    }

    /// <summary>Looks a presented key up. Unknown and revoked keys are not valid.</summary>
    public KeyCheck Check(string? presented)
    {
        if (string.IsNullOrWhiteSpace(presented) || !presented.StartsWith(Prefix, StringComparison.Ordinal))
            return new KeyCheck(false, 0, null);

        string hash = Hash(presented.Trim());
        using var c = db.Open();
        using var cmd = c.Command("SELECT id, key_hash, machine_id FROM api_keys WHERE key_hash = $h AND revoked IS NULL").With("$h", hash);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return new KeyCheck(false, 0, null);

        // The lookup already matched, but compare in constant time anyway so timing reveals nothing.
        bool same = CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(hash), Encoding.ASCII.GetBytes(r.GetString(1)));
        return new KeyCheck(same, r.GetInt64(0), r.GetStringOrNull(2));
    }

    /// <summary>Binds an unbound key to a machine and records that it was used.</summary>
    public void MarkUsed(long keyId, string machineId)
    {
        using var c = db.Open();
        using var cmd = c.Command("UPDATE api_keys SET last_used = $now, machine_id = coalesce(machine_id, $m) WHERE id = $id")
            .With("$id", keyId).With("$m", machineId).With("$now", UnixTime.Now);
        cmd.ExecuteNonQuery();
    }

    public void Revoke(long keyId)
    {
        using var c = db.Open();
        using var cmd = c.Command("UPDATE api_keys SET revoked = $now WHERE id = $id AND revoked IS NULL").With("$id", keyId).With("$now", UnixTime.Now);
        cmd.ExecuteNonQuery();
    }

    public List<ApiKeyInfo> List()
    {
        using var c = db.Open();
        using var cmd = c.Command("SELECT id, key_prefix, machine_id, label, created, last_used, revoked FROM api_keys ORDER BY revoked IS NOT NULL, created DESC");
        using var r = cmd.ExecuteReader();
        var list = new List<ApiKeyInfo>();
        while (r.Read())
        {
            list.Add(new ApiKeyInfo
            {
                Id = r.GetInt64(0), KeyPrefix = r.GetString(1), MachineId = r.GetStringOrNull(2), Label = r.GetString(3),
                Created = r.GetInt64(4), LastUsed = r.GetInt64OrNull(5), Revoked = r.GetInt64OrNull(6),
            });
        }
        return list;
    }

    public static string Hash(string key) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)));

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
