using VailIT.CodeClean.Configuration;

namespace VailIT.CodeClean.Rules;

public static partial class DefaultRules
{
    /// <summary>Private keys, certificates, signing keys, JWTs and other cryptographic material.</summary>
    public static IEnumerable<FilterRule> KeysAndCertificates()
    {
        const string c = CatKeys;

        yield return Rule("pem-private-key", "Private key block (PEM / OpenSSH / PGP)", c, Severity.Critical, "PRIVATE_KEY",
                """-----BEGIN[ A-Z0-9]*PRIVATE KEY(?: BLOCK)?-----(?<secret>[\s\S]+?)-----END[ A-Z0-9]*PRIVATE KEY(?: BLOCK)?-----""",
                "RSA, EC, DSA, OpenSSH, PKCS#8 (encrypted or not) and PGP private keys. The BEGIN/END lines are kept so readers know what was there; the key body is replaced line by line.",
                "-----BEGIN RSA PRIVATE KEY-----\n{{B64:64}}\n{{B64:64}}\n-----END RSA PRIVATE KEY-----")
            .Cs();

        yield return Rule("putty-private-key", "PuTTY private key (.ppk)", c, Severity.Critical, "PRIVATE_KEY",
                """Private-Lines:\s*\d+\s*[\r\n]+(?<secret>[\s\S]+?)(?=Private-MAC:)""",
                "The private part of a PuTTY key file.",
                "PuTTY-User-Key-File-3: ssh-rsa\nPrivate-Lines: 2\n{{B64:64}}\n{{B64:40}}\nPrivate-MAC: {{HEX:64}}")
            .Cs();

        yield return Rule("pem-certificate", "Certificate block (PEM)", c, Severity.Low, "CERTIFICATE",
                """-----BEGIN (?:CERTIFICATE|TRUSTED CERTIFICATE|X509 CRL|CERTIFICATE REQUEST|NEW CERTIFICATE REQUEST|PKCS7)-----(?<secret>[\s\S]+?)-----END (?:CERTIFICATE|TRUSTED CERTIFICATE|X509 CRL|CERTIFICATE REQUEST|NEW CERTIFICATE REQUEST|PKCS7)-----""",
                "Certificates are public, but they name your servers, organisation and internal CA. Body replaced, BEGIN/END kept.",
                "-----BEGIN CERTIFICATE-----\n{{B64:64}}\n-----END CERTIFICATE-----")
            .Cs();

        yield return Rule("ssh-public-key", "SSH public key", c, Severity.Medium, "SSH_PUBLIC_KEY",
                """\b(?:ssh-(?:rsa|dss|ed25519)|ecdsa-sha2-nistp(?:256|384|521)|sk-ssh-ed25519@openssh\.com)\s+(?<secret>AAAA[0-9A-Za-z+/]{40,}={0,3})(?:\s+(?<secret_KEY_COMMENT>[^\s"'<>]+@[^\s"'<>]+))?""",
                "authorized_keys / known_hosts entries. Public, but they identify people and machines (the user@host comment is replaced too).",
                "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5{{B64:48}} jsmith@build01")
            .Cs();

        yield return Rule("jwt", "JSON Web Token (JWT)", c, Severity.High, "JWT",
                """\beyJ[A-Za-z0-9_\-]{8,}\.eyJ[A-Za-z0-9_\-]{8,}\.[A-Za-z0-9_\-]{8,}""",
                "Signed tokens (often access or ID tokens) copied into code, tests or HTTP files.",
                "Authorization: eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.{{URLSAFE:43}}")
            .Cs();

        yield return Rule("machine-key", "ASP.NET <machineKey> validation / decryption key", c, Severity.Critical, "MACHINE_KEY",
                """(?i)\b(?:validationKey|decryptionKey)\s*=\s*"(?<secret>[A-F0-9]{16,})""",
                "web.config machineKey values (used to sign/encrypt view state, forms auth cookies).",
                """<machineKey validationKey="{{HEXU:64}}" decryptionKey="{{HEXU:48}}" />""");

        yield return Rule("kubeconfig-data", "Kubernetes kubeconfig key / certificate data", c, Severity.Critical, "KUBE_SECRET",
                """(?i)\b(?:client-key-data|client-certificate-data|certificate-authority-data|token|id-token|refresh-token)\s*:\s*["']?(?<secret>[A-Za-z0-9+/=_\-.]{40,})""",
                "Base64 client keys, certificates and tokens inside kubeconfig files.",
                "    client-key-data: LS0tLS1CRUdJTi{{B64:60}}")
            .Files("*.yml", "*.yaml", "*kubeconfig*", "config", "*.conf", "*.json");

        yield return Rule("k8s-secret-data", "Kubernetes Secret data values", c, Severity.High, "KUBE_SECRET",
                """(?m)^[ \t]+[\w.\-]+:[ \t]+(?<secret>[A-Za-z0-9+/]{16,}={0,2})[ \t]*(?=\r?$)""",
                "Base64 values in YAML (as used in kind: Secret manifests and Helm values). Only applied to YAML files and only to values that look random.",
                "data:\n  db-password: {{B64:24}}==")
            .Files("*.yml", "*.yaml").Entropy(3.5);

        yield return Rule("cert-thumbprint", "Certificate thumbprint", c, Severity.Medium, "THUMBPRINT",
                """(?i)\b(?:thumbprint|cert(?:ificate)?_?(?:thumbprint|hash|fingerprint)|ssl_?thumbprint|signing_?cert)["']?\s*(?:=|:|=>)\s*["']?(?<secret>(?:[A-F0-9]{2}[:\s]?){19}[A-F0-9]{2}|[A-F0-9]{64})\b""",
                "SHA-1/SHA-256 certificate thumbprints (they identify certificates in your stores).",
                """ "CertificateThumbprint": "{{HEXU:40}}" """);

        yield return Rule("pfx-password", "Certificate / keystore password", c, Severity.Critical, "PASSWORD",
                """(?i)(?:new\s+X509Certificate2\s*\([^,\r\n]+,\s*|(?:pfx|p12|cert(?:ificate)?|keystore|truststore|signing)_?(?:password|pass|pwd|passphrase)["']?\s*(?:=|:|=>)\s*@?)["'](?<secret>[^"'\r\n]{3,128})["']""",
                "Passwords for .pfx/.p12 files, Java keystores and code-signing certificates.",
                """var cert = new X509Certificate2("site.pfx", "Spring2024!");""")
            .IgnorePlaceholders();

        yield return Rule("byte-array-key", "Hard-coded key as a byte array", c, Severity.Medium, "BYTE_KEY",
                """(?i)(?:new\s+byte\s*\[\s*\d*\s*\]\s*|\[\]byte\s*|bytes\s*\(\s*\[?|=\s*)\{?\[?\s*(?<secret>(?:0x[0-9a-f]{2}\s*,\s*){15,}0x[0-9a-f]{2})""",
                "16+ hex bytes in a row, the usual way AES keys, IVs and salts are pasted into code.",
                """byte[] key = new byte[] { 0x1f, 0x2e, 0x3d, 0x4c, 0x5b, 0x6a, 0x79, 0x88, 0x97, 0xa6, 0xb5, 0xc4, 0xd3, 0xe2, 0xf1, 0x00 };""");

        yield return Rule("age-secret-key", "age encryption secret key", c, Severity.Critical, "PRIVATE_KEY",
                """\bAGE-SECRET-KEY-1[QPZRY9X8GF2TVDW0S3JN54KHCE6MUA7L]{58}\b""",
                "Secret keys for the age file encryption tool.",
                "AGE-SECRET-KEY-1{{BECH32:58}}")
            .Cs();

        yield return Rule("long-base64", "Long Base64 blob", c, Severity.Low, "BASE64_BLOB",
                """(?<![A-Za-z0-9+/=])[A-Za-z0-9+/]{120,}={0,2}(?![A-Za-z0-9+/=])""",
                "Very long Base64 strings (embedded keys, certificates, packed config). Off by default: it also hits images and fonts embedded in CSS/HTML.",
                "{{B64:160}}")
            .Cs().Entropy(4.5).Off();

        yield return Rule("generic-guid", "Any GUID", c, Severity.Low, "GUID",
                $$"""\b{{Guid}}\b""",
                "Every GUID. Off by default (project and COM GUIDs are everywhere); the Azure rules already catch tenant, subscription and client ids by context.",
                "{12345678-90ab-cdef-1234-567890abcdef}")
            .Off();
    }
}
