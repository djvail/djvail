using VailIT.CodeClean.Configuration;

namespace VailIT.CodeClean.Rules;

public static partial class DefaultRules
{
    /// <summary>Personal information that often ends up in test data, seed scripts, logs and comments.</summary>
    public static IEnumerable<FilterRule> PersonalData()
    {
        const string c = CatPersonal;

        yield return Rule("email", "Email address", c, Severity.Medium, "EMAIL",
                """(?i)(?<![\w.%+\-@])[a-z0-9][a-z0-9._%+\-]{0,63}@(?:[a-z0-9](?:[a-z0-9\-]{0,61}[a-z0-9])?\.)+[a-z]{2,24}\b""",
                "Email addresses of people, distribution lists and service accounts. example.com/org/net, noreply and git@ addresses are skipped.",
                "// Contact jane.smith@contoso.com before changing this")
            .IgnoreValue("""(?i)@(?:example\.(?:com|org|net)|test\.com|localhost|domain\.com|email\.com|company\.com|contoso\.com\.invalid|users\.noreply\.github\.com|noreply\.github\.com)$|^(?:git|noreply|no-reply|donotreply|do-not-reply|user|username|someone|test|nobody)@|\.(?:png|jpe?g|gif|svg|webp|css|js)$|@\d+(?:\.\d+)*x\.""");

        yield return Rule("phone-us", "Phone number (North America)", c, Severity.Medium, "PHONE",
                """(?<![\w+\-.])(?:\+?1[\s.\-])?\(?[2-9]\d{2}\)?[\s.\-][2-9]\d{2}[\s.\-]\d{4}(?![\w\-])""",
                "Numbers like (303) 555-0147, 303-555-0147, +1 303.555.0147.",
                "Call the on-call engineer at (303) 555-0147.")
            .IgnoreLine("""(?i)\b(?:version|build|guid|uuid|timestamp|epoch)\b""");

        yield return Rule("phone-international", "Phone number (international +CC format)", c, Severity.Medium, "PHONE",
                """(?<![\w+])\+(?:[1-9]\d{0,2})[\s.\-]?\(?\d{1,4}\)?(?:[\s.\-]?\d{2,4}){2,4}(?![\w\-])""",
                "E.164-style numbers such as +44 20 7946 0958 or +61-2-5550-1234. Off by default (can match arithmetic in code).",
                "Phone: +44 20 7946 0958")
            .Off();

        yield return Rule("us-ssn", "US Social Security Number", c, Severity.Critical, "SSN",
                """(?<![\w\-])(?!000|666|9\d\d)\d{3}-(?!00)\d{2}-(?!0000)\d{4}(?![\w\-])""",
                "123-45-6789 style numbers (invalid ranges excluded).",
                "INSERT INTO Employees VALUES ('Jane', '524-31-9876');");

        yield return Rule("credit-card", "Credit card number", c, Severity.Critical, "CARD_NUMBER",
                """(?<![\w\-])(?:4\d{12}(?:\d{3})?|(?:5[1-5]\d{2}|2[2-7]\d{2})\d{12}|3[47]\d{13}|6(?:011|5\d{2})\d{12}|3(?:0[0-5]|[68]\d)\d{11}|(?:4\d{3}|5[1-5]\d{2}|2[2-7]\d{2}|6011|65\d{2})(?:[ \-]\d{4}){3}|3[47]\d{2}[ \-]\d{6}[ \-]\d{5})(?![\w\-])""",
                "Visa, Mastercard, Amex, Discover and Diners numbers that pass the Luhn checksum. Well-known test numbers (4111 1111 1111 1111 etc.) are skipped.",
                "card: 4539 1488 0343 6467")
            .Luhn()
            .Ignore("4111111111111111", "4111 1111 1111 1111", "4111-1111-1111-1111", "4242424242424242", "4242 4242 4242 4242", "5555555555554444", "5555 5555 5555 4444", "378282246310005", "371449635398431", "6011111111111117", "4000056655665556", "5105105105105100", "4012888888881881");

        yield return Rule("iban", "Bank account (IBAN)", c, Severity.High, "IBAN",
                """\b[A-Z]{2}\d{2}(?:[ ]?[A-Z0-9]{4}){3,7}(?:[ ]?[A-Z0-9]{1,3})?\b""",
                "International bank account numbers.",
                "IBAN: GB82 WEST 1234 5698 7654 32")
            .Cs()
            .IgnoreLine("""(?i)\b(?:guid|uuid|hash|sha\d*|md5|token|key|version|base64)\b""");

        yield return Rule("us-bank-routing", "US bank routing / account number setting", c, Severity.High, "BANK_ACCOUNT",
                """(?i)\b(?:routing_?(?:number|no)|aba_?(?:number|routing)|account_?(?:number|no)|acct_?(?:num|no)|bank_?account)["']?\s*(?:=|:|=>)\s*["']?(?<secret>\d{6,17})\b""",
                "Routing and account numbers in settings, fixtures and seed data.",
                "\"routingNumber\": \"021000021\"");

        yield return Rule("passport-license", "Passport / driver licence / national id setting", c, Severity.High, "GOV_ID",
                """(?i)\b(?:passport_?(?:no|number|num)|driver'?s?_?licen[cs]e_?(?:no|number)?|dl_?number|national_?id|nin|nino|tax_?id|tin|ein|sin_?number|aadhaar|nhs_?number)["']?\s*(?:=|:|=>)\s*["']?(?<secret>[A-Z0-9\-]{6,20})\b""",
                "Government identifiers by setting name.",
                "\"passportNumber\": \"X12345678\"");

        yield return Rule("date-of-birth", "Date of birth", c, Severity.Medium, "DOB",
                """(?i)\b(?:dob|date_?of_?birth|birth_?date|birthday)["']?\s*(?:=|:|=>)\s*["']?(?<secret>\d{1,4}[\-/.]\d{1,2}[\-/.]\d{1,4})""",
                "Dates assigned to dob / dateOfBirth / birthDate.",
                "\"dateOfBirth\": \"1984-07-12\"");

        yield return Rule("person-name-field", "Person name in test / seed data", c, Severity.Low, "PERSON_NAME",
                QuotedAssignment("""first_?name|last_?name|full_?name|given_?name|surname|family_?name|display_?name|contact_?name|owner_?name|author_?name|employee_?name|customer_?name"""),
                "Quoted values assigned to firstName, lastName, fullName, displayName... Off by default: enable when you have real names in fixtures.",
                "new Customer { FirstName = \"Jane\", LastName = \"Smith\" }")
            .IgnorePlaceholders().Off()
            .Ignore("John", "Doe", "Jane", "John Doe", "Jane Doe", "Test", "User", "Test User", "Admin", "First", "Last", "Name", "Foo", "Bar");

        yield return Rule("street-address", "Street address (US style)", c, Severity.Low, "ADDRESS",
                """(?i)\b\d{1,6}\s+(?:[A-Z][a-z]+\s+){1,4}(?:Street|St|Avenue|Ave|Road|Rd|Boulevard|Blvd|Drive|Dr|Lane|Ln|Court|Ct|Way|Place|Pl|Parkway|Pkwy|Circle|Cir|Terrace|Highway|Hwy)\b\.?(?:,?\s+(?:Suite|Ste|Apt|Unit|#)\s*\w+)?""",
                "Street addresses in fixtures and comments. Off by default.",
                "Ship to 1600 Main Street, Suite 200")
            .Off();
    }
}
