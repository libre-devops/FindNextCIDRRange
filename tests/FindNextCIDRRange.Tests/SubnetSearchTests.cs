using FindNextCIDR;
using System.Net;

namespace FindNextCIDRRange.Tests;

public class SubnetSearchTests
{
    private static string Search(string vnetCidr, byte size, params string[] usedPrefixes)
    {
        return GetCidr.GetValidSubnetIfExists(usedPrefixes, IPNetwork2.Parse(vnetCidr), size);
    }

    [Fact]
    public void An_empty_vnet_yields_the_first_block()
    {
        Assert.Equal("10.30.0.0/26", Search("10.30.0.0/24", 26));
    }

    [Fact]
    public void The_next_free_block_after_existing_subnets_is_proposed()
    {
        // The standalone example stack: a /24 with two /26 subnets already carved.
        Assert.Equal("10.30.0.128/26", Search("10.30.0.0/24", 26, "10.30.0.0/26", "10.30.0.64/26"));
    }

    [Fact]
    public void A_gap_between_subnets_is_filled_first()
    {
        Assert.Equal("10.30.0.64/26", Search("10.30.0.0/24", 26, "10.30.0.0/26", "10.30.0.128/26"));
    }

    [Fact]
    public void A_partially_overlapping_candidate_is_skipped()
    {
        // 10.30.0.32/27 sits inside the first /26 candidate, so the search moves past it.
        Assert.Equal("10.30.0.64/26", Search("10.30.0.0/24", 26, "10.30.0.32/27"));
    }

    [Fact]
    public void A_full_vnet_yields_nothing()
    {
        Assert.Null(Search("10.30.0.0/24", 26, "10.30.0.0/25", "10.30.0.128/25"));
    }

    [Fact]
    public void The_whole_address_space_is_a_valid_answer_when_empty()
    {
        Assert.Equal("10.30.0.0/24", Search("10.30.0.0/24", 24));
    }
}
