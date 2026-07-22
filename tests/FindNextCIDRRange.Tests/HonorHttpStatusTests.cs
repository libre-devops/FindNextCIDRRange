using FindNextCIDR;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;

namespace FindNextCIDRRange.Tests;

// HONOR_HTTP_STATUS is the opt-in escape hatch from the historical always-400 wire status: when a
// deployment sets it to true, errors travel with the status from the body's code field. Default
// off, and the body bytes are identical in both modes; only the status line may differ.
public class HonorHttpStatusTests
{
    [Theory]
    [InlineData(false, HttpStatusCode.BadRequest, HttpStatusCode.BadRequest)]
    [InlineData(false, HttpStatusCode.NotFound, HttpStatusCode.BadRequest)]
    [InlineData(false, HttpStatusCode.InternalServerError, HttpStatusCode.BadRequest)]
    [InlineData(true, HttpStatusCode.BadRequest, HttpStatusCode.BadRequest)]
    [InlineData(true, HttpStatusCode.NotFound, HttpStatusCode.NotFound)]
    [InlineData(true, HttpStatusCode.InternalServerError, HttpStatusCode.InternalServerError)]
    public void The_wire_status_is_400_unless_the_deployment_opts_in(bool honor, HttpStatusCode intended, HttpStatusCode expected)
    {
        Assert.Equal(expected, GetCidr.WireStatus(honor, intended));
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("True", true)]
    [InlineData("TRUE", true)]
    [InlineData("false", false)]
    [InlineData("1", false)]
    [InlineData("yes", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void The_setting_only_engages_on_a_boolean_true(string value, bool expected)
    {
        string original = Environment.GetEnvironmentVariable("HONOR_HTTP_STATUS");
        try
        {
            Environment.SetEnvironmentVariable("HONOR_HTTP_STATUS", value);
            Assert.Equal(expected, GetCidr.ReadHonorHttpStatus());
        }
        finally
        {
            Environment.SetEnvironmentVariable("HONOR_HTTP_STATUS", original);
        }
    }

    [Fact]
    public async Task Opting_in_changes_nothing_about_the_body_or_a_true_400()
    {
        var query = "?subscriptionId=x&resourceGroupName=x&virtualNetworkName=x&cidr=55";

        var honest = await InvokeWith(honorHttpStatus: true, query);
        var historical = await InvokeWith(honorHttpStatus: false, query);

        // A validation failure really is a 400, so both modes agree on the wire, and the body and
        // content type must be byte-identical between modes.
        Assert.Equal(HttpStatusCode.BadRequest, honest.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, historical.StatusCode);
        Assert.Equal(historical.ReadBody(), honest.ReadBody());
        Assert.True(honest.Headers.TryGetValues("Content-Type", out var contentType));
        Assert.Equal("text/plain; charset=utf-8", Assert.Single(contentType));
    }

    private static async Task<FakeHttpResponseData> InvokeWith(bool honorHttpStatus, string queryString)
    {
        var function = new GetCidr(NullLogger<GetCidr>.Instance, honorHttpStatus);
        var request = new FakeHttpRequestData(
            new FakeFunctionContext(),
            new Uri("https://localhost/api/GetCidr" + queryString));

        return (FakeHttpResponseData)await function.Run(request);
    }
}
