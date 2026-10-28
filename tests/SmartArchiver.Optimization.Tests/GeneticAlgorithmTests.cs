using SmartArchiver.Optimization;

namespace SmartArchiver.Optimization.Tests;

public sealed class GeneticAlgorithmTests
{
    [Fact]
    public void BasicChromosome_UsesUniformCrossoverAndValidMutation()
    {
        var space = new BasicChromosomeSpace();
        var random = new Random(7);
        BasicChromosome first = new(1_024, 3, 0.01);
        BasicChromosome second = new(32_768, 20, 0.20);

        (BasicChromosome child1, BasicChromosome child2) = space.Crossover(first, second, random);
        child1 = space.Mutate(child1, random, 1.0);
        child2 = space.Mutate(child2, random, 1.0);

        child1.Validate();
        child2.Validate();
        Assert.Contains(child1.SearchWindow, new[] { 256, 512, 1_024, 2_048, 4_096, 8_192, 16_384, 32_768 });
    }

    [Fact]
    public void GeneticAndRandomSearch_UseExactlyTheSameEvaluationBudget()
    {
        var space = new BasicChromosomeSpace();
        var fitness = new SimpleFitness();
        var options = new GeneticSearchOptions(PopulationSize: 6, Generations: 4, EliteCount: 2, Seed: 3);

        SearchRun<BasicChromosome> genetic = new GeneticAlgorithm<BasicChromosome>(space, fitness).Run(options);
        SearchRun<BasicChromosome> random = new RandomSearch<BasicChromosome>(space, fitness).Run(options, seed: 4);

        Assert.Equal(30, genetic.FitnessEvaluations);
        Assert.Equal(genetic.FitnessEvaluations, random.FitnessEvaluations);
        Assert.True(genetic.FinalGenerationBest >= genetic.InitialGenerationBest);
    }

    [Fact]
    public void FullChromosome_UsesTwoPointCrossoverAndAdaptiveSearchGenes()
    {
        var space = new FullChromosomeSpace();
        var random = new Random(11);
        FullChromosome first = FullChromosome.Default;
        FullChromosome second = FullChromosome.Default with
        {
            SearchWindow = 1_024,
            MinMatchLength = 12,
            SearchDepth = 17,
            HuffmanBlockSize = 4_096,
            HistoryDepth = 4,
            PerceptronArchitecture = PerceptronArchitecture.Wide,
            Strategies = StrategyFlags.UsePrediction,
        };

        (FullChromosome child1, FullChromosome child2) = space.Crossover(first, second, random);
        child1.Validate();
        child2.Validate();
        Assert.NotEmpty(space.Neighbours(child1));
        Assert.StartsWith("{", ChromosomeJson.Serialize(child1));
    }

    [Fact]
    public void ParetoFront_KeepsOnlyNonDominatedCompressionSpeedPairs()
    {
        var candidates = new[]
        {
            Candidate(0, 0.8, 0.4),
            Candidate(1, 0.7, 0.7),
            Candidate(2, 0.6, 0.3),
        };

        IReadOnlyList<EvaluatedCandidate<int>> front = ParetoFront.Compute(candidates);

        Assert.Equal(new[] { 0, 1 }, front.Select(x => x.Chromosome));
    }

    private static EvaluatedCandidate<int> Candidate(int id, double compression, double speed) =>
        new(SearchMethod.Genetic, 0, id, id, new FitnessResult(
            Fitness: 0.5 * compression + 0.5 * speed,
            Compression: compression,
            Speed: speed,
            Seconds: 1,
            ArchiveBytes: 1,
            AllRestored: true));

    private sealed class SimpleFitness : IFitnessFunction<BasicChromosome>
    {
        public FitnessResult Evaluate(BasicChromosome chromosome) => new(
            chromosome.AbandonThreshold,
            chromosome.AbandonThreshold,
            1 - chromosome.AbandonThreshold,
            1,
            1,
            true);
    }
}
