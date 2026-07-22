using FindNextCIDR;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using System.Text.Json;

namespace FindNextCIDRRange.Tests;

// CheckCidr postdates the 1.x contract, so unlike GetCidr it answers with truthful wire statuses
// and application/json: 200 for any parseable CIDR (a "not allowed in Azure" verdict is still a
// successful answer), 400 only for input that cannot be parsed. Nothing here touches Azure.
public class CheckCidrTests
{
    private static async Task<FakeHttpResponseData> Invoke(string queryString, string method = "GET", string body = null)
    {
        var function = new CheckCidr(NullLogger<CheckCidr>.Instance);
        var request = new FakeHttpRequestData(
            new FakeFunctionContext(),
            new Uri("https://localhost/api/CheckCidr" + queryString),
            method,
            body);

        return (FakeHttpResponseData)await function.Run(request);
    }

    private static CheckCidr.CheckCidrResponse Analyze(string cidr)
    {
        Assert.True(CheckCidr.TryAnalyze(cidr, out var result, out _));
        return result;
    }

    [Fact]
    public async Task A_valid_subnet_answers_200_as_json_with_the_azure_arithmetic()
    {
        var response = await Invoke("?cidr=10.0.0.0/29");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("Content-Type", out var contentType));
        Assert.Equal("application/json; charset=utf-8", Assert.Single(contentType));

        var result = JsonSerializer.Deserialize<CheckCidr.CheckCidrResponse>(response.ReadBody());
        Assert.True(result.validAzureSubnet);
        Assert.Equal(8, result.totalAddresses);
        Assert.Equal(5, result.azureReservedAddresses);
        Assert.Equal(3, result.usableAddresses);
        Assert.Equal("10.0.0.0", result.reserved.networkAddress);
        Assert.Equal("10.0.0.1", result.reserved.defaultGateway);
        Assert.Equal(new[] { "10.0.0.2", "10.0.0.3" }, result.reserved.azureDns);
        Assert.Equal("10.0.0.7", result.reserved.broadcast);
        Assert.Equal("10.0.0.4", result.firstUsable);
        Assert.Equal("10.0.0.6", result.lastUsable);
        Assert.Null(result.reason);
    }

    [Fact]
    public async Task A_slash_30_is_a_200_with_a_no_and_the_reason_why()
    {
        var response = await Invoke("?cidr=10.0.0.0/30");
        var result = JsonSerializer.Deserialize<CheckCidr.CheckCidrResponse>(response.ReadBody());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(result.validAzureSubnet);
        Assert.Equal(4, result.totalAddresses);
        Assert.Equal(0, result.usableAddresses);
        Assert.Null(result.reserved);
        Assert.Contains("reserves 5 addresses", result.reason);
    }

    [Theory]
    [InlineData("", "cidr is null")]
    [InlineData("?cidr=", "cidr is null")]
    [InlineData("?cidr=banana", "cidr must include a prefix length, for example 10.0.0.0/24")]
    [InlineData("?cidr=10.0.0.0", "cidr must include a prefix length, for example 10.0.0.0/24")]
    [InlineData("?cidr=banana/24", "not a valid CIDR: banana/24")]
    [InlineData("?cidr=10.0.0.999/24", "not a valid CIDR: 10.0.0.999/24")]
    [InlineData("?cidr=10.0.0.0/33", "not a valid CIDR: 10.0.0.0/33")]
    [InlineData("?cidr=2001:db8::/64", "only IPv4 CIDRs are supported; Azure IPv6 subnets are always /64")]
    public async Task Unparseable_input_is_a_true_400_with_the_reason(string queryString, string expected)
    {
        var response = await Invoke(queryString);
        var error = JsonSerializer.Deserialize<CheckCidr.CheckCidrError>(response.ReadBody());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(expected, error.error);
    }

    [Fact]
    public async Task Post_reads_the_query_string_and_ignores_the_body()
    {
        var response = await Invoke("?cidr=10.0.0.0/29", method: "POST", body: "cidr=10.0.0.0/30");
        var result = JsonSerializer.Deserialize<CheckCidr.CheckCidrResponse>(response.ReadBody());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(result.validAzureSubnet);
        Assert.Equal(8, result.totalAddresses);
    }

    [Fact]
    public void Host_bits_are_dropped_and_reported_via_the_normalized_form()
    {
        var result = Analyze("10.0.0.5/24");

        Assert.Equal("10.0.0.5/24", result.cidr);
        Assert.Equal("10.0.0.0/24", result.normalized);
        Assert.True(result.validAzureSubnet);
    }

    [Theory]
    [InlineData("10.0.0.0/2", true)]
    [InlineData("10.0.0.0/29", true)]
    [InlineData("10.0.0.0/1", false)]
    [InlineData("0.0.0.0/0", false)]
    [InlineData("10.0.0.0/30", false)]
    [InlineData("10.0.0.0/31", false)]
    [InlineData("10.0.0.0/32", false)]
    public void Azure_allows_subnets_from_slash_2_through_slash_29(string cidr, bool expected)
    {
        Assert.Equal(expected, Analyze(cidr).validAzureSubnet);
    }

    [Fact]
    public void A_slash_24_loses_exactly_five_addresses_to_azure()
    {
        var result = Analyze("192.168.1.0/24");

        Assert.Equal(256, result.totalAddresses);
        Assert.Equal(251, result.usableAddresses);
        Assert.Equal("192.168.1.0", result.reserved.networkAddress);
        Assert.Equal("192.168.1.1", result.reserved.defaultGateway);
        Assert.Equal(new[] { "192.168.1.2", "192.168.1.3" }, result.reserved.azureDns);
        Assert.Equal("192.168.1.255", result.reserved.broadcast);
        Assert.Equal("192.168.1.4", result.firstUsable);
        Assert.Equal("192.168.1.254", result.lastUsable);
    }
}
