namespace SmartArchiver.Data.Entities;

public enum PackingMethod
{
    Dp,
    Greedy,
}

/// <summary>How one packing method filled one volume.</summary>
public sealed class PackingResult
{
    public int Id { get; set; }

    public int ExperimentId { get; set; }

    public Experiment Experiment { get; set; } = null!;

    public PackingMethod Method { get; set; }

    /// <summary>volume_index from FORMAT_SPEC_P2, from 0.</summary>
    public int VolumeIndex { get; set; }

    /// <summary>Knapsack capacity W in bytes.</summary>
    public long CapacityBytes { get; set; }

    /// <summary>Sum of the weights (coded_len + 15) of the chosen blocks.</summary>
    public long UsedBytes { get; set; }

    /// <summary>Sum of the values of the chosen blocks.</summary>
    public double TotalValue { get; set; }

    public int BlockCount { get; set; }

    public double ElapsedSeconds { get; set; }
}
