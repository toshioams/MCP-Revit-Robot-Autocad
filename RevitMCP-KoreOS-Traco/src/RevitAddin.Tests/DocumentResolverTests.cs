using RevitMCPAddin.Commands;
using Xunit;

namespace RevitMCPAddin.Tests;

public class DocumentResolverTests
{
    [Theory]
    [InlineData("teste mcp", null, "teste mcp", null, true)]
    [InlineData("teste mcp", null, "TESTE MCP.rvt", null, true)]
    [InlineData("_ARQPWR_Showroom R01_2025", null, "teste mcp", null, false)]
    [InlineData("teste mcp", @"C:\Users\Mana02\Desktop\teste mcp.rvt", null, "c:/users/mana02/desktop/teste mcp.rvt", true)]
    [InlineData("teste mcp", @"C:\a\teste mcp.rvt", "teste mcp", @"C:\b\teste mcp.rvt", false)]   // path wins over title
    [InlineData("teste mcp", "", null, @"C:\a\teste mcp.rvt", false)]                              // unsaved doc has no path
    public void Matches_title_or_path(string docTitle, string? docPath, string? title, string? path, bool expected)
        => Assert.Equal(expected, DocumentResolver.Matches(docTitle, docPath, title, path));
}
