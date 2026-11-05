using System.Globalization;
using SmartArchiver.Data.Entities;

namespace SmartArchiver.Data.Tests;

public class GeneticJournalCsvTests
{
    private static GeneticIndividual Individual(
        SearchAlgorithm algorithm, int generation, int individual, string genes, double fitness = 0.5) => new()
    {
        Algorithm = algorithm,
        Generation = generation,
        Individual = individual,
        GenesJson = genes,
        Fitness = fitness,
        S = 0.75,
        T = 0.25,
    };

    private static string[] Lines(IEnumerable<GeneticIndividual> individuals)
    {
        using var writer = new StringWriter();
        GeneticJournalCsv.Write(individuals, writer);
        return writer.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
    }

    [Fact]
    public void Write_HeaderHasFixedColumnsThenGenesInOrderOfFirstAppearance()
    {
        string[] lines = Lines([Individual(SearchAlgorithm.Genetic, 0, 0, """{"window":4096,"min_match":3}""")]);

        Assert.Equal("algorithm,generation,individual,fitness,s,t,window,min_match", lines[0]);
        Assert.Equal("Genetic,0,0,0.5,0.75,0.25,4096,3", lines[1]);
    }

    [Fact]
    public void Write_GeneMissingInOneChromosome_LeavesItsCellEmpty()
    {
        string[] lines = Lines(
        [
            Individual(SearchAlgorithm.Genetic, 0, 0, """{"window":4096}"""),
            Individual(SearchAlgorithm.Genetic, 0, 1, """{"window":1024,"block_size":65536}"""),
        ]);

        Assert.Equal("algorithm,generation,individual,fitness,s,t,window,block_size", lines[0]);
        Assert.Equal("Genetic,0,0,0.5,0.75,0.25,4096,", lines[1]);
        Assert.Equal("Genetic,0,1,0.5,0.75,0.25,1024,65536", lines[2]);
    }

    [Fact]
    public void Write_RowsAreOrderedByAlgorithmGenerationAndIndividual()
    {
        string[] lines = Lines(
        [
            Individual(SearchAlgorithm.Random, 0, 0, "{}"),
            Individual(SearchAlgorithm.Genetic, 1, 0, "{}"),
            Individual(SearchAlgorithm.Genetic, 0, 1, "{}"),
            Individual(SearchAlgorithm.Genetic, 0, 0, "{}"),
        ]);

        Assert.Equal(
            ["Genetic,0,0", "Genetic,0,1", "Genetic,1,0", "Random,0,0"],
            lines.Skip(1).Select(line => string.Join(',', line.Split(',').Take(3))));
    }

    [Fact]
    public void Write_NumbersUseInvariantCulture_WhateverTheCurrentCulture()
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("uk-UA");
        try
        {
            string[] lines = Lines([Individual(SearchAlgorithm.Genetic, 0, 0, """{"threshold":7.25}""", fitness: 0.125)]);

            Assert.Equal("Genetic,0,0,0.125,0.75,0.25,7.25", lines[1]);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Write_TextAndBooleanGenes_AreWrittenAsValues_WithCommasQuoted()
    {
        string[] lines = Lines([Individual(SearchAlgorithm.Genetic, 0, 0, """{"layers":"38,16,7","lazy":true,"name":"a\"b"}""")]);

        Assert.Equal("Genetic,0,0,0.5,0.75,0.25,\"38,16,7\",true,\"a\"\"b\"", lines[1]);
    }

    [Fact]
    public void Write_NoIndividuals_WritesOnlyTheFixedHeader()
    {
        Assert.Equal(["algorithm,generation,individual,fitness,s,t"], Lines([]));
    }

    [Fact]
    public void Write_GenesNotAnObject_Throws()
    {
        Assert.Throws<FormatException>(() => Lines([Individual(SearchAlgorithm.Genetic, 0, 0, "[1,2,3]")]));
    }
}
