using HikvisionReplicator.Api.Infrastructure.Isapi;

namespace HikvisionReplicator.Tests.Infrastructure.Isapi;

/// <summary>
/// How a spectator is keyed and named on a reader (A-2, ISA-26). The hash vectors are
/// computed outside .NET (<c>printf 'TICKET-0001' | sha256sum</c>, first 32 hex characters), so
/// a change to the encoding, the digest or the slice fails here rather than silently orphaning
/// every spectator already on a reader.
/// </summary>
public class IsapiIdentityTests
{
    // ─── A-2: the reader key ───

    [Fact]
    public void The_reader_key_is_the_first_half_of_the_sha256_of_the_reference_in_lowercase_hex()
    {
        Assert.Equal("b43a95a2c19a97b6289861e47299284f", IsapiIdentity.EmployeeNo("TICKET-0001"));
    }

    [Fact]
    public void The_same_reference_always_gives_the_same_key()
    {
        Assert.Equal(IsapiIdentity.EmployeeNo("TICKET-0001"), IsapiIdentity.EmployeeNo("TICKET-0001"));
    }

    [Fact]
    public void References_one_character_apart_get_different_keys()
    {
        Assert.Equal("c00f7e79e9885159077c70bae32caca4", IsapiIdentity.EmployeeNo("TICKET-0002"));
        Assert.NotEqual(IsapiIdentity.EmployeeNo("TICKET-0001"), IsapiIdentity.EmployeeNo("TICKET-0002"));
    }

    [Fact]
    public void A_reference_of_the_maximum_length_still_gives_a_32_character_hex_key()
    {
        var key = IsapiIdentity.EmployeeNo(new string('R', 255));

        Assert.Equal("cd61096cd82567d184db29ec08699518", key);
        Assert.Matches("^[0-9a-f]{32}$", key);
    }

    [Fact]
    public void A_non_ascii_reference_is_hashed_as_utf8_into_an_ascii_key()
    {
        var key = IsapiIdentity.EmployeeNo("Bilhete-Ação-Ñ");

        Assert.Equal("f07dc11db4d539d0320da2242a1bf516", key);
        Assert.Matches("^[0-9a-f]{32}$", key);
    }

    // ─── ISA-26: a name is fitted, never refused ───

    [Fact]
    public void A_name_under_the_limit_is_kept_whole()
    {
        Assert.Equal("Ana Souza", IsapiIdentity.TruncateName("Ana Souza", 32));
    }

    [Fact]
    public void A_name_exactly_at_the_limit_is_kept_whole()
    {
        Assert.Equal("Ana Souza", IsapiIdentity.TruncateName("Ana Souza", 9));
    }

    [Fact]
    public void A_name_over_the_limit_is_cut_to_the_limit()
    {
        Assert.Equal("Maria Apar", IsapiIdentity.TruncateName("Maria Aparecida da Silva", 10));
    }

    [Fact]
    public void A_cut_through_a_surrogate_pair_drops_the_whole_pair()
    {
        // "Jo" + U+1F600 (two UTF-16 units) + "ao": a cut at 3 would keep half the emoji.
        var name = "Jo😀ao";

        Assert.Equal("Jo", IsapiIdentity.TruncateName(name, 3));
    }

    [Fact]
    public void A_cut_through_a_combining_sequence_drops_the_whole_sequence()
    {
        // "Jose" with the acute as a combining mark: a cut at 4 would strip the accent off the e.
        var name = "José Silva";

        Assert.Equal("Jos", IsapiIdentity.TruncateName(name, 4));
    }

    [Fact]
    public void A_combining_sequence_that_fits_is_kept_with_its_mark()
    {
        Assert.Equal("José", IsapiIdentity.TruncateName("José Silva", 5));
    }
}
