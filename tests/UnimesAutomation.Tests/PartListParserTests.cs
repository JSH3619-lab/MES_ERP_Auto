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
}
