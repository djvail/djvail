using VailIT.CodeClean.Configuration;

namespace VailIT.CodeClean.Rules;

public static partial class DefaultRules
{
    /// <summary>
    /// Templates for organisation-specific rules. They are all OFF: edit the pattern in the Rules tab
    /// (or here) with your own company names, domains, project code names and id formats, then switch them on.
    /// </summary>
    public static IEnumerable<FilterRule> Custom()
    {
        const string c = CatCustom;

        yield return Rule("custom-company-names", "Company & product names (edit me)", c, Severity.Medium, "COMPANY",
                "Contoso\nFabrikam\nNorthwind Traders",
                "Exact text, one per line. Replace these examples with your company, subsidiary, client and product names.",
                "// Copyright Contoso Ltd")
            .Exact(wholeWord: true).Off();

        yield return Rule("custom-domains", "Company domains (edit me)", c, Severity.High, "DOMAIN",
                """(?i)\b(?:[a-z0-9\-]+\.)*(?:contoso\.com|contoso\.local|fabrikam\.net)\b""",
                "Regex for your public and internal DNS domains, including every sub-domain.",
                "https://intranet.contoso.com/home")
            .Off();

        yield return Rule("custom-server-naming", "Server naming convention (edit me)", c, Severity.High, "SERVER",
                """(?i)\b(?:PRD|PROD|UAT|STG|DEV|QA|DR)[\-_]?(?:SQL|DB|APP|WEB|DC|FS|SRV|VM|K8S|ESX|BKP)[\-_]?\d{1,3}[a-z]?\b""",
                "Matches names such as PROD-SQL01, UAT_WEB2, DEVAPP003. Change it to match how your servers are named.",
                "Server=PROD-SQL01;")
            .Off();

        yield return Rule("custom-project-codenames", "Project code names (edit me)", c, Severity.Medium, "PROJECT",
                "Project Falcon\nBluebird\nOperation Nightjar",
                "Exact text, one per line: internal project, programme or customer code names.",
                "// TODO: remove before Bluebird launch")
            .Exact(wholeWord: true).Off();

        yield return Rule("custom-employee-id", "Employee / customer id format (edit me)", c, Severity.Medium, "EMPLOYEE_ID",
                """\b(?:EMP|CUST|ACC)-?\d{5,8}\b""",
                "Example id formats like EMP-004512 or CUST12345678. Adjust to your own.",
                "assignedTo = EMP-004512")
            .Cs().Off();

        yield return Rule("custom-ticket-links", "Internal ticket references (edit me)", c, Severity.Low, "TICKET",
                """\b(?:FIN|HR|SEC|OPS)-\d{2,6}\b""",
                "Ticket keys from internal projects whose names are sensitive. Replace the keys with your own.",
                "// Fix for SEC-1432")
            .Cs().Off();

        yield return Rule("custom-sensitive-line", "Remove whole line containing a marker (edit me)", c, Severity.High, "REMOVED_LINE",
                "DO-NOT-SHARE\nCONFIDENTIAL\nINTERNAL ONLY",
                "Exact markers: the WHOLE line containing one of them is replaced (Scope = Line).",
                "// CONFIDENTIAL: pricing formula agreed with partner")
            .Exact(wholeWord: false).WholeLine().Off();
    }
}
