namespace SmartArchiver.Optimization;

public enum SearchMethod
{
    Genetic,
    Random,
    LocalSearch,
}

public sealed record EvaluatedCandidate<T>(
    SearchMethod Algorithm,
    int Generation,
    int Individual,
    T Chromosome,
    FitnessResult Result);

public sealed record SearchRun<T>(
    IReadOnlyList<EvaluatedCandidate<T>> Evaluations,
    T BestChromosome,
    FitnessResult BestResult,
    double InitialGenerationBest,
    double FinalGenerationBest)
{
    public int FitnessEvaluations => Evaluations.Count;
}

/// <summary>Budget and operators for a GA run.</summary>
public sealed record GeneticSearchOptions(
    int PopulationSize = 12,
    int Generations = 10,
    int EliteCount = 2,
    int TournamentSize = 3,
    double InitialMutationRate = 0.25,
    double FinalMutationRate = 0.05,
    int Seed = 2026);

public sealed record LocalSearchOptions(int MaxEvaluations = 100, int Seed = 2026);

/// <summary>
/// Generational GA with tournament selection, crossover, adaptive mutation and
/// elitism. Elites are evaluated again, so the evaluation budget is explicit and
/// can be matched exactly by RandomSearch.
/// </summary>
public sealed class GeneticAlgorithm<T>
{
    private readonly IChromosomeSpace<T> space;
    private readonly IFitnessFunction<T> fitness;

    public GeneticAlgorithm(IChromosomeSpace<T> space, IFitnessFunction<T> fitness)
    {
        this.space = space ?? throw new ArgumentNullException(nameof(space));
        this.fitness = fitness ?? throw new ArgumentNullException(nameof(fitness));
    }

    public SearchRun<T> Run(GeneticSearchOptions options)
    {
        Validate(options);
        var random = new Random(options.Seed);
        var journal = new List<EvaluatedCandidate<T>>(checked(options.PopulationSize * (options.Generations + 1)));

        List<T> population = Enumerable.Range(0, options.PopulationSize)
            .Select(_ => space.CreateRandom(random))
            .ToList();
        List<EvaluatedCandidate<T>> scored = Evaluate(
            population,
            SearchMethod.Genetic,
            generation: 0,
            journal,
            options.PopulationSize);
        double initialBest = scored.Max(x => x.Result.Fitness);
        EvaluatedCandidate<T> best = scored.Aggregate(Better);

        for (int generation = 1; generation <= options.Generations; generation++)
        {
            List<EvaluatedCandidate<T>> ranked = [.. scored.OrderByDescending(x => x.Result.Fitness)];
            var next = new List<T>(options.PopulationSize);
            for (int i = 0; i < options.EliteCount; i++)
                next.Add(ranked[i].Chromosome);

            int stagnation = generation - LastImprovementGeneration(journal, generation - 1);
            double mutationRate = AdaptiveMutationRate(options, generation, stagnation);
            while (next.Count < options.PopulationSize)
            {
                T parent1 = Tournament(ranked, random, options.TournamentSize).Chromosome;
                T parent2 = Tournament(ranked, random, options.TournamentSize).Chromosome;
                (T child1, T child2) = space.Crossover(parent1, parent2, random);
                next.Add(space.Mutate(child1, random, mutationRate));
                if (next.Count < options.PopulationSize)
                    next.Add(space.Mutate(child2, random, mutationRate));
            }

            // Elites still call the fitness function. This makes the GA and the
            // random baseline use exactly the same number of evaluations.
            List<EvaluatedCandidate<T>> fresh = Evaluate(
                next,
                SearchMethod.Genetic,
                generation,
                journal,
                options.PopulationSize);
            for (int i = 0; i < options.EliteCount; i++)
            {
                EvaluatedCandidate<T> retained = ranked[i];
                EvaluatedCandidate<T> rechecked = fresh[i];
                fresh[i] = rechecked with { Result = BetterResult(retained.Result, rechecked.Result) };
                journal[journal.Count - options.PopulationSize + i] = fresh[i];
            }
            scored = fresh;
            EvaluatedCandidate<T> generationBest = scored.Aggregate(Better);
            best = Better(best, generationBest);
        }

        double finalBest = journal
            .Where(x => x.Algorithm == SearchMethod.Genetic && x.Generation == options.Generations)
            .Select(x => x.Result.Fitness)
            .DefaultIfEmpty(initialBest)
            .Max();
        return new SearchRun<T>(journal, best.Chromosome, best.Result, initialBest, finalBest);
    }

    private List<EvaluatedCandidate<T>> Evaluate(
        List<T> chromosomes,
        SearchMethod method,
        int generation,
        List<EvaluatedCandidate<T>> journal,
        int expectedCount)
    {
        if (chromosomes.Count != expectedCount)
            throw new InvalidOperationException("Покоління має зберігати сталий розмір.");
        var result = new List<EvaluatedCandidate<T>>(chromosomes.Count);
        for (int individual = 0; individual < chromosomes.Count; individual++)
        {
            var evaluated = new EvaluatedCandidate<T>(
                method,
                generation,
                individual,
                chromosomes[individual],
                fitness.Evaluate(chromosomes[individual]));
            result.Add(evaluated);
            journal.Add(evaluated);
        }
        return result;
    }

    private static EvaluatedCandidate<T> Tournament(
        List<EvaluatedCandidate<T>> population,
        Random random,
        int size)
    {
        EvaluatedCandidate<T> result = population[random.Next(population.Count)];
        for (int i = 1; i < size; i++)
        {
            EvaluatedCandidate<T> candidate = population[random.Next(population.Count)];
            result = Better(result, candidate);
        }
        return result;
    }

    private static EvaluatedCandidate<T> Better(EvaluatedCandidate<T> first, EvaluatedCandidate<T> second) =>
        BetterResult(first.Result, second.Result).Equals(first.Result) ? first : second;

    private static FitnessResult BetterResult(FitnessResult first, FitnessResult second)
    {
        int byFitness = second.Fitness.CompareTo(first.Fitness);
        if (byFitness < 0)
            return first;
        if (byFitness > 0)
            return second;
        int byCompression = second.Compression.CompareTo(first.Compression);
        return byCompression <= 0 ? first : second;
    }

    private static int LastImprovementGeneration(IReadOnlyList<EvaluatedCandidate<T>> journal, int before)
    {
        double best = double.NegativeInfinity;
        int last = 0;
        foreach (EvaluatedCandidate<T> row in journal.Where(x => x.Algorithm == SearchMethod.Genetic && x.Generation <= before)
                     .OrderBy(x => x.Generation))
        {
            if (row.Result.Fitness > best)
            {
                best = row.Result.Fitness;
                last = row.Generation;
            }
        }
        return last;
    }

    private static double AdaptiveMutationRate(GeneticSearchOptions options, int generation, int stagnation)
    {
        double progress = options.Generations == 0 ? 1 : (double)generation / options.Generations;
        double rate = options.InitialMutationRate +
            (options.FinalMutationRate - options.InitialMutationRate) * progress;
        if (stagnation >= 3)
            rate *= Math.Min(3.0, 1.0 + 0.25 * (stagnation - 2));
        return Math.Clamp(rate, 0, 1);
    }

    private static void Validate(GeneticSearchOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.PopulationSize < 1 || options.Generations < 0 || options.EliteCount < 0 || options.EliteCount > options.PopulationSize)
            throw new ArgumentOutOfRangeException(nameof(options));
        if (options.TournamentSize < 1 || options.TournamentSize > options.PopulationSize)
            throw new ArgumentOutOfRangeException(nameof(options), options.TournamentSize, "TournamentSize має бути від 1 до розміру популяції.");
        BasicChromosomeSpace.ValidateRate(options.InitialMutationRate);
        BasicChromosomeSpace.ValidateRate(options.FinalMutationRate);
    }
}

/// <summary>Random baseline with the exact same number of evaluations as a GA budget.</summary>
public sealed class RandomSearch<T>
{
    private readonly IChromosomeSpace<T> space;
    private readonly IFitnessFunction<T> fitness;

    public RandomSearch(IChromosomeSpace<T> space, IFitnessFunction<T> fitness)
    {
        this.space = space ?? throw new ArgumentNullException(nameof(space));
        this.fitness = fitness ?? throw new ArgumentNullException(nameof(fitness));
    }

    public SearchRun<T> Run(GeneticSearchOptions budget, int? seed = null)
    {
        ArgumentNullException.ThrowIfNull(budget);
        if (budget.PopulationSize < 1 || budget.Generations < 0)
            throw new ArgumentOutOfRangeException(nameof(budget));
        var random = new Random(seed ?? budget.Seed + 1);
        var journal = new List<EvaluatedCandidate<T>>(checked(budget.PopulationSize * (budget.Generations + 1)));
        EvaluatedCandidate<T>? best = null;
        double initial = 0;
        double final = 0;
        for (int generation = 0; generation <= budget.Generations; generation++)
        {
            double generationBest = double.NegativeInfinity;
            for (int individual = 0; individual < budget.PopulationSize; individual++)
            {
                T chromosome = space.CreateRandom(random);
                var row = new EvaluatedCandidate<T>(SearchMethod.Random, generation, individual, chromosome, fitness.Evaluate(chromosome));
                journal.Add(row);
                if (best is null || IsBetter(row, best))
                    best = row;
                generationBest = Math.Max(generationBest, row.Result.Fitness);
            }
            if (generation == 0)
                initial = generationBest;
            if (generation == budget.Generations)
                final = generationBest;
        }
        EvaluatedCandidate<T> finalBest = best ?? throw new InvalidOperationException("Випадковий пошук не оцінив жодної хромосоми.");
        return new SearchRun<T>(journal, finalBest.Chromosome, finalBest.Result, initial, final);
    }

    private static bool IsBetter(EvaluatedCandidate<T> first, EvaluatedCandidate<T> second) =>
        first.Result.Fitness > second.Result.Fitness ||
        (first.Result.Fitness == second.Result.Fitness && first.Result.Compression > second.Result.Compression);
}

/// <summary>Simple first-improvement local search around one chromosome.</summary>
public sealed class LocalSearch<T>
{
    private readonly IChromosomeSpace<T> space;
    private readonly IFitnessFunction<T> fitness;

    public LocalSearch(IChromosomeSpace<T> space, IFitnessFunction<T> fitness)
    {
        this.space = space ?? throw new ArgumentNullException(nameof(space));
        this.fitness = fitness ?? throw new ArgumentNullException(nameof(fitness));
    }

    public SearchRun<T> Run(T start, LocalSearchOptions options)
    {
        ArgumentNullException.ThrowIfNull(start);
        ArgumentNullException.ThrowIfNull(options);
        if (options.MaxEvaluations < 1)
            throw new ArgumentOutOfRangeException(nameof(options), options.MaxEvaluations, "MaxEvaluations має бути не менше 1.");
        var random = new Random(options.Seed);
        var journal = new List<EvaluatedCandidate<T>>(options.MaxEvaluations);
        T current = start;
        var first = new EvaluatedCandidate<T>(SearchMethod.LocalSearch, 0, 0, current, fitness.Evaluate(current));
        journal.Add(first);
        EvaluatedCandidate<T> best = first;
        int generation = 0;

        while (journal.Count < options.MaxEvaluations)
        {
            IReadOnlyList<T> neighbours = space.Neighbours(current);
            if (neighbours.Count == 0)
                break;
            bool improved = false;
            foreach (T candidate in neighbours.OrderBy(_ => random.Next()))
            {
                if (journal.Count >= options.MaxEvaluations)
                    break;
                var row = new EvaluatedCandidate<T>(SearchMethod.LocalSearch, generation + 1, journal.Count, candidate, fitness.Evaluate(candidate));
                journal.Add(row);
                if (row.Result.Fitness > best.Result.Fitness)
                {
                    best = row;
                    current = candidate;
                    improved = true;
                    break;
                }
            }
            generation++;
            if (!improved)
                break;
        }

        double initial = first.Result.Fitness;
        double final = journal.Where(x => x.Generation == journal.Max(y => y.Generation)).Max(x => x.Result.Fitness);
        return new SearchRun<T>(journal, best.Chromosome, best.Result, initial, final);
    }
}
