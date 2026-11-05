namespace SmartArchiver.Optimization;

/// <summary>
/// A chromosome is dominated when another one is no worse in both compression
/// and speed and is strictly better in at least one of them.
/// </summary>
public static class ParetoFront
{
    public static IReadOnlyList<EvaluatedCandidate<T>> Compute<T>(IEnumerable<EvaluatedCandidate<T>> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        List<EvaluatedCandidate<T>> all = [.. candidates];
        var front = new List<EvaluatedCandidate<T>>();
        for (int i = 0; i < all.Count; i++)
        {
            EvaluatedCandidate<T> candidate = all[i];
            bool dominated = false;
            for (int j = 0; j < all.Count; j++)
            {
                if (i == j)
                    continue;
                FitnessResult other = all[j].Result;
                FitnessResult current = candidate.Result;
                if (other.Compression >= current.Compression &&
                    other.Speed >= current.Speed &&
                    (other.Compression > current.Compression || other.Speed > current.Speed))
                {
                    dominated = true;
                    break;
                }
            }
            if (!dominated)
                front.Add(candidate);
        }

        return [.. front
            .OrderByDescending(x => x.Result.Compression)
            .ThenByDescending(x => x.Result.Speed)
            .ThenBy(x => x.Algorithm)
            .ThenBy(x => x.Generation)
            .ThenBy(x => x.Individual)];
    }
}
