namespace SmartArchiver.Data.Entities;

/// <summary>How the chromosomes of a search were produced.</summary>
public enum SearchAlgorithm
{
    /// <summary>Genetic algorithm: selection, crossover, mutation, elitism.</summary>
    Genetic,

    /// <summary>Random search with the same number of fitness evaluations, the baseline for the GA.</summary>
    Random,

    /// <summary>Local search around the best chromosome.</summary>
    LocalSearch,
}

/// <summary>
/// One chromosome of the GA evolution journal and its fitness. Every evaluated chromosome is kept, not only the best
/// of a generation: the convergence table needs the best and the mean per generation, the Pareto front needs them all.
/// </summary>
public sealed class GeneticIndividual
{
    public int Id { get; set; }

    public int ExperimentId { get; set; }

    public Experiment Experiment { get; set; } = null!;

    public SearchAlgorithm Algorithm { get; set; }

    /// <summary>Generation from 0; for random search, the batch of the same size as a GA population.</summary>
    public int Generation { get; set; }

    /// <summary>Position in the generation from 0.</summary>
    public int Individual { get; set; }

    /// <summary>Genes as a JSON object, for example {"window":4096,"min_match":3}; the full chromosome has more of them.</summary>
    public string GenesJson { get; set; } = "{}";

    /// <summary>Fitness the search maximised, Q of the chromosome.</summary>
    public double Fitness { get; set; }

    /// <summary>Compression component of Q, kept apart for the Pareto front.</summary>
    public double S { get; set; }

    /// <summary>Speed component of Q, kept apart for the Pareto front.</summary>
    public double T { get; set; }
}
