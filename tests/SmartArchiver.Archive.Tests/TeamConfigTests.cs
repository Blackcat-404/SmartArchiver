namespace SmartArchiver.Archive.Tests;

public class TeamConfigTests
{
    [Fact]
    public void Parse_PrioritiesAsArray()
    {
        TeamConfig config = TeamConfig.Parse("""{ "max_volume_bytes": 65536, "пріоритети_типів": [5, 4, 3, 2, 1, 0, 9] }""");

        Assert.Equal(65536, config.MaxVolumeBytes);
        Assert.Equal(5, config.Priorities.ValueOf(FileKind.Text));
        Assert.Equal(9, config.Priorities.ValueOf(FileKind.Artificial));
    }

    [Fact]
    public void Parse_PrioritiesAsObject()
    {
        TeamConfig config = TeamConfig.Parse(
            """{ "max_volume_bytes": 1000, "пріоритети_типів": {"0":1,"1":2,"2":3,"3":4,"4":5,"5":6,"6":7} }""");

        Assert.Equal(6, config.Priorities.ValueOf(FileKind.AlreadyCompressed));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{ "max_volume_bytes": 1000 }""")]
    [InlineData("""{ "пріоритети_типів": [1,2,3,4,5,6,7] }""")]
    [InlineData("""{ "max_volume_bytes": 1000, "пріоритети_типів": [1,2,3] }""")]
    [InlineData("""{ "max_volume_bytes": 1000, "пріоритети_типів": [1,2,3,4,5,6,-7] }""")]
    [InlineData("""{ "max_volume_bytes": 1000, "пріоритети_типів": {"0":1} }""")]
    [InlineData("не json")]
    public void Parse_BadConfig_IsRejected(string json)
    {
        Assert.Throws<ArgumentException>(() => TeamConfig.Parse(json));
    }

    [Fact]
    public void UnknownKind_GetsTheSmallestPriority()
    {
        var priorities = new PackingPriorities([5, 4, 3, 2, 7, 6, 9]);

        Assert.Equal(2, priorities.ValueOf(FileKind.Unknown));
    }

    [Fact]
    public void Default_HasOneValuePerKind()
    {
        Assert.Equal(PackingPriorities.KindCount, PackingPriorities.Default.Values.Count);
    }
}
