using System.Linq;
using UnimesAutomation;
using Xunit;

public class PartListParserTests
{
    [Fact]
    public void Splits_on_newline_comma_space_and_dedupes()
    {
        var parts = PartListParser.Parse("RM1, RM2\nRM3 RM2");
        Assert.Equal(new[] { "RM1", "RM2", "RM3" }, parts.Select(p => p.PartNo).ToArray());
    }

    [Fact]
    public void Empty_input_yields_empty()
    {
        Assert.Empty(PartListParser.Parse("   \n  "));
    }

    [Fact]
    public void FromCodes_drops_unknown_prefix_noise()
    {
        var parts = PartListParser.FromCodes(["품목코드", "MES - Mining", "DUMMY", "ZCA8G485WE-5BVRX"]);
        Assert.Equal(new[] { "ZCA8G485WE-5BVRX" }, parts.Select(p => p.PartNo).ToArray());
    }

    [Fact]
    public void FromCodes_drops_incoming_z4_keeps_classified()
    {
        var parts = PartListParser.FromCodes(["Z4A8G485WE-5VXEL", "ZCA8G485WE-5BVRX"]);
        Assert.Equal(new[] { "ZCA8G485WE-5BVRX" }, parts.Select(p => p.PartNo).ToArray());
    }

    [Fact]
    public void FromCodes_dedupes_variants_to_pid()
    {
        var parts = PartListParser.FromCodes(
        [
            "SNAKGD8J0B-HZRV32",
            "SNAKGD8J0B-HZRV32-TPDGA0T",
            "SNAKGD8J0B-HZRV32-TPCGB0T",
        ]);
        Assert.Equal(new[] { "SNAKGD8J0B-HZRV32" }, parts.Select(p => p.PartNo).ToArray());
    }

    [Fact]
    public void FromCodes_drops_dummy_ending_00_keeps_0Y()
    {
        var parts = PartListParser.FromCodes(["DABHGD8J5F-ARRXZ3100", "DABHGD8J5F-ARRSZ31Z0Y"]);
        Assert.Equal(new[] { "DABHGD8J5F-ARRSZ31Z0Y" }, parts.Select(p => p.PartNo).ToArray());
    }
}
