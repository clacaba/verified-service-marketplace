using System.Net;
using System.Runtime.CompilerServices;
using System.Xml;
using System.Xml.Linq;

[assembly: InternalsVisibleTo("Marketplace.Api.Tests")]

namespace Marketplace.Api.Verification;

internal static class PrivateBucketPolicy
{
    internal static void Validate(HttpStatusCode status, string? body)
    {
        // AWS SDK v4 can return an S3-compatible 404 error body as Policy
        // instead of throwing. Accept only the explicit missing-policy error.
        if (status == HttpStatusCode.NotFound && IsMissingPolicy(body)) return;
        // Dedicated evidence bucket must have no policy. All other errors,
        // malformed responses, and nonempty policies fail closed.
        if (status != HttpStatusCode.OK || !string.IsNullOrWhiteSpace(body))
            throw new EvidenceUnavailableException();
    }

    private static bool IsMissingPolicy(string? body)
    {
        if (string.IsNullOrWhiteSpace(body) || body.Length > 4096) return false;
        try
        {
            using var reader = XmlReader.Create(new StringReader(body), new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = 4096
            });
            var root = XDocument.Load(reader).Root;
            return root?.Name == "Error" && root.Elements("Code").Count() == 1
                && root.Element("Code")!.Value == "NoSuchBucketPolicy";
        }
        catch (XmlException) { return false; }
    }
}
