using FindNextCIDR;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using System.Text.Json;

namespace FindNextCIDRRange.Tests;

// These tests pin the wire contract the 2.x rewrite promised to preserve: every error travels as
// HTTP 400 with the meaningful status in the body's code field, bodies are indented JSON served as
// text/plain, and POST reads the query string. They drive the real function end to end; only
// requests that fail validation are used, so nothing ever reaches Azure.
public class HttpContractTests
{
    private static async Task<FakeHttpResponseData> Invoke(string queryString, string method = "GET", string body = null)
    {
        // The default mode is pinned explicitly so the ambient HONOR_HTTP_STATUS environment
        // variable can never leak into these assertions.
        var function = new GetCidr(NullLogger<GetCidr>.Instance, honorHttpStatus: false);
        var request = new FakeHttpRequestData(
            new FakeFunctionContext(),
            new Uri("https://localhost/api/GetCidr" + queryString),
            method,
            body);

        return (FakeHttpResponseData)await function.Run(request);
    }

    [Fact]
    public async Task Every_error_is_400_on_the_wire_served_as_text_plain()
    {
        var response = await Invoke("?subscriptionId=x&resourceGroupName=x&virtualNetworkName=x&cidr=55");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("Content-Type", out var contentType));
        Assert.Equal("text/plain; charset=utf-8", Assert.Single(contentType));
    }

    [Fact]
    public async Task An_invalid_cidr_size_produces_the_exact_historical_body()
    {
        var response = await Invoke("?subscriptionId=x&resourceGroupName=x&virtualNetworkName=x&cidr=55");

        var expected = "{\n  \"code\": \"400\",\n  \"message\": \"BadRequest, Invalid CIDR size requested: 55\"\n}";
        Assert.Equal(expected, response.ReadBody().Replace("\r\n", "\n"));
    }

    [Theory]
    [InlineData("", "subscriptionId is null")]
    [InlineData("?subscriptionId=x", "virtualNetworkName is null")]
    [InlineData("?subscriptionId=x&virtualNetworkName=x", "resourceGroupName is null")]
    [InlineData("?subscriptionId=x&virtualNetworkName=x&resourceGroupName=x", "cidr is null")]
    [InlineData("?subscriptionId=&virtualNetworkName=x&resourceGroupName=x&cidr=26", "subscriptionId is null")]
    [InlineData("?subscriptionId=x&virtualNetworkName=&resourceGroupName=x&cidr=26", "virtualNetworkName is null")]
    [InlineData("?subscriptionId=x&virtualNetworkName=x&resourceGroupName=&cidr=26", "resourceGroupName is null")]
    public async Task Missing_parameters_report_which_one_in_the_body(string queryString, string detail)
    {
        var response = await Invoke(queryString);
        var error = JsonSerializer.Deserialize<GetCidr.CustomError>(response.ReadBody());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("400", error.code);
        Assert.Equal("BadRequest, Invalid input: " + detail, error.message);
    }

    [Fact]
    public async Task An_empty_cidr_keeps_its_own_historical_body()
    {
        var response = await Invoke("?subscriptionId=x&resourceGroupName=x&virtualNetworkName=x&cidr=");
        var error = JsonSerializer.Deserialize<GetCidr.CustomError>(response.ReadBody());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("400", error.code);
        Assert.Equal("BadRequest, Invalid CIDR size requested: ", error.message);
    }

    [Fact]
    public async Task A_malformed_address_space_is_rejected_before_touching_azure()
    {
        var response = await Invoke("?subscriptionId=x&resourceGroupName=x&virtualNetworkName=x&cidr=26&addressSpace=not-a-cidr");
        var error = JsonSerializer.Deserialize<GetCidr.CustomError>(response.ReadBody());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("400", error.code);
        Assert.Equal("BadRequest, Invalid input: desiredAddressSpace is invalid", error.message);
    }

    [Fact]
    public async Task Post_reads_the_query_string_and_ignores_the_body()
    {
        var response = await Invoke(
            "?subscriptionId=x&resourceGroupName=x&virtualNetworkName=x&cidr=55",
            method: "POST",
            body: "subscriptionId=y&resourceGroupName=y&virtualNetworkName=y&cidr=26");
        var error = JsonSerializer.Deserialize<GetCidr.CustomError>(response.ReadBody());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("400", error.code);
        Assert.Equal("BadRequest, Invalid CIDR size requested: 55", error.message);
    }
}
