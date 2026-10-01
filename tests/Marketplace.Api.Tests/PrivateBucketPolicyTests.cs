using System.Net;
using Marketplace.Api.Verification;
using Xunit;

namespace Marketplace.Api.Tests;

public sealed class PrivateBucketPolicyTests
{
    [Theory]
    [InlineData(200, "")]
    [InlineData(200, " ")]
    [InlineData(404, "<?xml version=\"1.0\"?><Error><Code>NoSuchBucketPolicy</Code><Message>The bucket policy does not exist</Message></Error>")]
    public void AcceptsOnlySuccessfulEmptyPolicyOrExplicitMissingPolicy(int status, string body)
        => PrivateBucketPolicy.Validate((HttpStatusCode)status, body);

    [Theory]
    [InlineData(200, "{\"Statement\":[{\"Effect\":\"Allow\"}]}")]
    [InlineData(200, "<Error><Code>NoSuchBucketPolicy</Code></Error>")]
    [InlineData(403, "")]
    [InlineData(403, "<Error><Code>NoSuchBucketPolicy</Code></Error>")]
    [InlineData(500, "<Error><Code>NoSuchBucketPolicy</Code></Error>")]
    [InlineData(404, "")]
    [InlineData(404, "<Error><Code>NoSuchBucket</Code></Error>")]
    [InlineData(404, "<Error><Code>AccessDenied</Code></Error>")]
    [InlineData(404, "NoSuchBucketPolicy")]
    [InlineData(404, "<Error><Code>NoSuchBucketPolicy")]
    [InlineData(404, "<Error><Code>NoSuchBucketPolicy</Code><Code>AccessDenied</Code></Error>")]
    [InlineData(404, "<!DOCTYPE Error [<!ENTITY code 'NoSuchBucketPolicy'>]><Error><Code>&code;</Code></Error>")]
    public void DeniesPoliciesErrorsAndMalformedResponses(int status, string body)
        => Assert.Throws<EvidenceUnavailableException>(() => PrivateBucketPolicy.Validate((HttpStatusCode)status, body));
}
