using System.Globalization;
using System.Text.Json;
using SmartArchiver.Data.Entities;

namespace SmartArchiver.Data;

/// <summary>
/// Exports the GA evolution journal as CSV, the copy of the journal that is kept in the repository.
/// One row per chromosome: fixed columns, then one column per gene in the order genes first appear.
/// </summary>
public static class GeneticJournalCsv
{
    private const string FixedHeader = "algorithm,generation,individual,fitness,s,t";

    /// <summary>Lines end with \n and numbers use the invariant culture, so the file is the same on every machine.</summary>
    /// <exception cref="FormatException">A chromosome's genes are not a JSON object.</exception>
    public static void Write(IEnumerable<GeneticIndividual> individuals, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(individuals);
        ArgumentNullException.ThrowIfNull(writer);

        var rows = individuals
            .OrderBy(i => i.Algorithm)
            .ThenBy(i => i.Generation)
            .ThenBy(i => i.Individual)
            .Select(i => (Individual: i, Genes: ParseGenes(i.GenesJson)))
            .ToList();
        var geneNames = new List<string>();
        foreach (string name in rows.SelectMany(r => r.Genes.Keys))
        {
            if (!geneNames.Contains(name))
                geneNames.Add(name);
        }

        writer.Write(FixedHeader);
        foreach (string name in geneNames)
            writer.Write("," + Escape(name));
        writer.Write('\n');

        foreach ((GeneticIndividual individual, Dictionary<string, string> genes) in rows)
        {
            writer.Write(string.Join(',',
                individual.Algorithm.ToString(),
                individual.Generation.ToString(CultureInfo.InvariantCulture),
                individual.Individual.ToString(CultureInfo.InvariantCulture),
                individual.Fitness.ToString("R", CultureInfo.InvariantCulture),
                individual.S.ToString("R", CultureInfo.InvariantCulture),
                individual.T.ToString("R", CultureInfo.InvariantCulture)));
            foreach (string name in geneNames)
                writer.Write("," + (genes.TryGetValue(name, out string? value) ? Escape(value) : ""));
            writer.Write('\n');
        }
    }

    /// <summary>Gene name → value as text; numbers and booleans keep their JSON spelling, strings lose their quotes.</summary>
    private static Dictionary<string, string> ParseGenes(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new FormatException($"Genes must be a JSON object: {json}");

        var genes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (JsonProperty gene in document.RootElement.EnumerateObject())
        {
            genes[gene.Name] = gene.Value.ValueKind == JsonValueKind.String
                ? gene.Value.GetString()!
                : gene.Value.GetRawText();
        }
        return genes;
    }

    /// <summary>RFC 4180: a value with a comma, quote or line break is quoted, inner quotes are doubled.</summary>
    private static string Escape(string value) =>
        value.AsSpan().IndexOfAny(",\"\r\n") < 0 ? value : "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}
