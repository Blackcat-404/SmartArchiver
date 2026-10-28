using System.Diagnostics;
using SmartArchiver.Analysis;
using SmartArchiver.Archive;
using SmartArchiver.Compression;
using SmartArchiver.Core;

namespace SmartArchiver.Optimization;

/// <summary>The two objective components and the scalar score used by the GA.</summary>
public sealed record FitnessResult(
    double Fitness,
    double Compression,
    double Speed,
    double Seconds,
    long ArchiveBytes,
    bool AllRestored);

public interface IFitnessFunction<in T>
{
    FitnessResult Evaluate(T chromosome);
}

/// <summary>Settings shared by all chromosome evaluations of one corpus.</summary>
public sealed record CorpusFitnessSettings(
    IReadOnlyList<(ArchiveInput Input, byte Group)> Corpus,
    PackOptions BaseOptions,
    double Alpha,
    double Beta,
    double TRefSeconds)
{
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Corpus);
        ArgumentNullException.ThrowIfNull(BaseOptions);
        if (Alpha < 0 || Beta < 0 || Alpha + Beta <= 0)
            throw new ArgumentOutOfRangeException(nameof(Alpha), "Ваги Q мають бути невід'ємними, а їх сума — додатною.");
        if (TRefSeconds <= 0 || double.IsNaN(TRefSeconds) || double.IsInfinity(TRefSeconds))
            throw new ArgumentOutOfRangeException(nameof(TRefSeconds));
        if (Corpus.Count == 0)
            throw new ArgumentException("Корпус для оптимізації не може бути порожнім.", nameof(Corpus));
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach ((ArchiveInput input, _) in Corpus)
        {
            ArgumentNullException.ThrowIfNull(input);
            if (!names.Add(input.Name))
                throw new ArgumentException($"У корпусі повторюється ім'я {input.Name}.", nameof(Corpus));
        }
    }
}

/// <summary>
/// Fitness of a chromosome is the actual SmartArchiver Q metric: the full pipeline
/// is executed, protected volumes are read back, and S and T are retained separately.
/// </summary>
public sealed class CorpusFitnessEvaluator : IFitnessFunction<BasicChromosome>, IFitnessFunction<FullChromosome>
{
    private readonly CorpusFitnessSettings settings;
    private readonly Dictionary<(int History, PerceptronArchitecture Architecture), BytePredictor> predictors = [];

    public CorpusFitnessEvaluator(CorpusFitnessSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();
        this.settings = settings;
    }

    public FitnessResult Evaluate(BasicChromosome chromosome)
    {
        ArgumentNullException.ThrowIfNull(chromosome);
        chromosome.Validate();
        return Evaluate(FullChromosome.FromBasic(chromosome));
    }

    public FitnessResult Evaluate(FullChromosome chromosome)
    {
        ArgumentNullException.ThrowIfNull(chromosome);
        chromosome.Validate();

        try
        {
            PackOptions options = MakeOptions(chromosome);
            return chromosome.Strategies.HasFlag(StrategyFlags.UsePrediction)
                ? MeasureWithPrediction(chromosome, options)
                : MeasurePlain(options);
        }
        catch (Exception ex) when (ex is ArchiveException or ArgumentException or InvalidOperationException or IOException)
        {
            // A legal chromosome can still produce an archive that does not fit a
            // configured volume. Such a candidate is simply infeasible and gets Q=0.
            return new FitnessResult(0, 0, 0, 0, 0, false);
        }
    }

    private FitnessResult MeasurePlain(PackOptions options)
    {
        QMeasurement measurement = CorpusMeasurer.Measure(
            settings.Corpus,
            options,
            settings.Alpha,
            settings.Beta,
            settings.TRefSeconds);
        return new FitnessResult(
            measurement.Score.Q,
            measurement.Score.S,
            measurement.Score.T,
            measurement.Seconds,
            measurement.ArchiveBytes,
            measurement.AllRestored);
    }

    private FitnessResult MeasureWithPrediction(FullChromosome chromosome, PackOptions options)
    {
        BytePredictor predictor = GetPredictor(chromosome.HistoryDepth, chromosome.PerceptronArchitecture);
        var watch = Stopwatch.StartNew();
        var transformed = new List<(ArchiveInput Input, byte Group)>(settings.Corpus.Count);
        foreach ((ArchiveInput input, byte group) in settings.Corpus)
        {
            transformed.Add((
                input with { Data = predictor.Encode(input.Data) },
                group));
        }

        List<ArchiveInput> inputs = [.. transformed.Select(x => x.Input)];
        IReadOnlyList<PreparedFile> prepared = ArchivePipeline.Prepare(inputs, options);
        IReadOnlyList<byte[]> volumes = ArchivePipeline.Seal(prepared, options);
        ArchiveResult<IReadOnlyList<ExtractedFile>> unpacked = ArchivePipeline.TryUnpack(volumes, options.Password);

        bool allRestored = unpacked.Ok;
        if (unpacked.Ok)
        {
            Dictionary<string, byte[]> restored = unpacked.Value!.ToDictionary(
                file => file.Name,
                file => file.Data,
                StringComparer.Ordinal);
            foreach ((ArchiveInput input, _) in settings.Corpus)
            {
                if (!restored.TryGetValue(input.Name, out byte[]? encoded) ||
                    encoded is null ||
                    !TryDecode(predictor, encoded, input.Data))
                {
                    allRestored = false;
                    break;
                }
            }
        }

        watch.Stop();
        long originalBytes = settings.Corpus.Sum(x => x.Input.Data.LongLength);
        long archiveBytes = volumes.Sum(volume => (long)volume.Length);
        QScore score = QMetric.Compute(
            settings.Alpha,
            settings.Beta,
            settings.TRefSeconds,
            originalBytes,
            archiveBytes,
            watch.Elapsed.TotalSeconds,
            allRestored);
        return new FitnessResult(
            score.Q,
            score.S,
            score.T,
            watch.Elapsed.TotalSeconds,
            archiveBytes,
            allRestored);
    }

    private PackOptions MakeOptions(FullChromosome chromosome)
    {
        StrategyFlags flags = chromosome.Strategies;
        IFileAnalyzer analyzer = flags.HasFlag(StrategyFlags.UseClassifier)
            ? settings.BaseOptions.Analyzer
            : AlwaysCompressAnalyzer.Instance;

        Lz77ParseMode parseMode = flags.HasFlag(StrategyFlags.UseIterativeParser)
            ? Lz77ParseMode.Iterative
            : flags.HasFlag(StrategyFlags.UseDpParser)
                ? Lz77ParseMode.Dp
                : Lz77ParseMode.Lazy;
        var search = new Lz77SearchOptions
        {
            SearchWindow = chromosome.SearchWindow,
            MinMatchLength = chromosome.MinMatchLength,
            SearchDepth = chromosome.SearchDepth,
            AbandonThreshold = chromosome.AbandonThreshold,
            AllowStoredBlocks = flags.HasFlag(StrategyFlags.AllowStoredBlocks),
        };

        return settings.BaseOptions with
        {
            Analyzer = analyzer,
            BlockSize = chromosome.HuffmanBlockSize,
            Algorithm = flags.HasFlag(StrategyFlags.UseDpPacking) ? PackingAlgorithm.Dp : PackingAlgorithm.Greedy,
            Scope = flags.HasFlag(StrategyFlags.PerBlockStrategy) ? StrategyScope.Block : StrategyScope.File,
            ParseMode = parseMode,
            SplitMode = BlockSplitMode.Fixed,
            Compression = search,
        };
    }

    private BytePredictor GetPredictor(int history, PerceptronArchitecture architecture)
    {
        var key = (history, architecture);
        if (predictors.TryGetValue(key, out BytePredictor? predictor))
            return predictor!;

        int hidden = architecture switch
        {
            PerceptronArchitecture.Compact => 16,
            PerceptronArchitecture.Standard => 32,
            PerceptronArchitecture.Wide => 64,
            _ => throw new ArgumentOutOfRangeException(nameof(architecture)),
        };
        predictor = BytePredictor.Train(
            settings.Corpus.Select(x => (ReadOnlyMemory<byte>)x.Input.Data),
            new PredictorOptions(
                ContextBytes: history,
                HiddenNeurons: hidden,
                Epochs: 1,
                LearningRate: 0.05,
                Seed: 1,
                MaxPositionsPerSample: 512));
        predictors[key] = predictor;
        return predictor;
    }

    private static bool TryDecode(BytePredictor predictor, byte[] encoded, byte[] expected)
    {
        try
        {
            return predictor.Decode(encoded).AsSpan().SequenceEqual(expected);
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    private sealed class AlwaysCompressAnalyzer : IFileAnalyzer
    {
        public static AlwaysCompressAnalyzer Instance { get; } = new();

        public FileAnalysis Analyze(ReadOnlySpan<byte> data) => new(
            FileKind.Text,
            1.0,
            data.IsEmpty ? CompressionStrategy.NoCompression : CompressionStrategy.Lz77Huffman);
    }
}
