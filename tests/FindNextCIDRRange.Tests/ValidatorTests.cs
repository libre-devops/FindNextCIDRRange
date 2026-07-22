using FindNextCIDR;

namespace FindNextCIDRRange.Tests;

public class ValidatorTests
{
    [Theory]
    [InlineData("2", true)]
    [InlineData("29", true)]
    [InlineData("16", true)]
    [InlineData("1", false)]
    [InlineData("30", false)]
    [InlineData("0", false)]
    [InlineData("255", false)]
    [InlineData("256", false)]
    [InlineData("-1", false)]
    [InlineData("abc", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Cidr_size_is_accepted_only_between_2_and_29(string cidr, bool expected)
    {
        Assert.Equal(expected, GetCidr.ValidateCIDR(cidr));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("10.0.0.0/24", true)]
    [InlineData("10.30.0.0/24", true)]
    [InlineData("not-a-cidr", false)]
    [InlineData("10.0.0.999/24", false)]
    [InlineData("", false)]
    public void Address_space_is_optional_but_must_parse_when_present(string addressSpace, bool expected)
    {
        Assert.Equal(expected, GetCidr.ValidateCIDRBlock(addressSpace));
    }

    [Fact]
    public void Missing_parameters_are_reported_in_a_fixed_order()
    {
        Assert.Equal("subscriptionId is null", GetCidr.ValidateInput(null, null, null, null, null));
        Assert.Equal("virtualNetworkName is null", GetCidr.ValidateInput("sub", null, null, null, null));
        Assert.Equal("resourceGroupName is null", GetCidr.ValidateInput("sub", "vnet", null, null, null));
        Assert.Equal("cidr is null", GetCidr.ValidateInput("sub", "vnet", "rg", null, null));
    }

    [Fact]
    public void A_bad_address_space_fails_input_validation()
    {
        Assert.Equal("desiredAddressSpace is invalid", GetCidr.ValidateInput("sub", "vnet", "rg", "26", "not-a-cidr"));
    }

    [Fact]
    public void A_complete_request_passes_input_validation()
    {
        Assert.Null(GetCidr.ValidateInput("sub", "vnet", "rg", "26", null));
        Assert.Null(GetCidr.ValidateInput("sub", "vnet", "rg", "26", "10.30.0.0/24"));
    }
}
