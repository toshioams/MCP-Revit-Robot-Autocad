using RevitMCPAddin.Commands;
using Xunit;

namespace RevitMCPAddin.Tests;

public class RecentDocumentsTests
{
    // Shape of the live Revit 2026 Revit.ini on 11/09/2026 (paths shortened).
    static readonly string[] Ini =
    {
        "[Directories]", "File1=C:\\not\\recent.rvt",
        "[Recent File List]",
        "File2=E:\\Dropbox\\MANÁ\\_ARQPWR_Showroom R01_2026.rvt",
        "File1=C:\\Users\\Mana02\\Desktop\\teste mcp.rvt",
        "File3=E:\\Dropbox\\MANÁ\\_ARQPWR_Showroom R01_2025.rvt",
        "File4=E:\\Dropbox\\TAPURAH\\250415_GINASIO_ARQ_TAPURAH_R05_2026.rvt",
        "File5=E:\\Dropbox\\TAPURAH\\250415_GINASIO_ARQ_TAPURAH_R05.rvt",
        "File6=e:/dropbox/tapurah/250415_GINASIO_ARQ_TAPURAH_R05.rvt",
        "File7=",
        "[Recent Workset List]", "Config1=All",
    };

    [Fact]
    public void Parse_reads_only_the_recent_section_in_order_without_duplicates()
    {
        var list = RecentDocuments.Parse(Ini);
        Assert.Equal(5, list.Count);
        Assert.Equal("C:\\Users\\Mana02\\Desktop\\teste mcp.rvt", list[0]);
        Assert.EndsWith("R01_2026.rvt", list[1]);
        Assert.DoesNotContain(list, p => p.Contains("not\\recent"));
    }

    [Fact]
    public void Normalize_drops_accents_case_and_separators()
        => Assert.Equal("250415 ginasio arq tapurah r05", RecentDocuments.Normalize("250415_GINÁSIO_ARQ-TAPURAH (R05)"));

    [Fact]
    public void Spoken_words_match_regardless_of_accents()
    {
        var list = RecentDocuments.Parse(Ini);
        var m = RecentDocuments.Match("ginásio tapurah 2026", list);
        Assert.Single(m);
        Assert.EndsWith("R05_2026.rvt", m[0]);
    }

    [Fact]
    public void Ambiguous_name_returns_every_candidate()
        => Assert.Equal(2, RecentDocuments.Match("showroom", RecentDocuments.Parse(Ini)).Count);

    [Fact]
    public void Exact_file_name_wins_over_longer_names_containing_it()
    {
        var m = RecentDocuments.Match("250415_GINASIO_ARQ_TAPURAH_R05", RecentDocuments.Parse(Ini));
        Assert.Single(m);
        Assert.EndsWith("TAPURAH_R05.rvt", m[0]);
    }

    [Fact]
    public void No_match_and_blank_query_return_empty()
    {
        var list = RecentDocuments.Parse(Ini);
        Assert.Empty(RecentDocuments.Match("escola rangel", list));
        Assert.Empty(RecentDocuments.Match("  _ ", list));
    }
}
