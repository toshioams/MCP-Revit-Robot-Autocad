using System.Text.Json.Nodes;
using RevitMCPAddin.Server;
using Xunit;

namespace RevitMCPAddin.Tests;

/// <summary>
/// A preview must never commit. On 11/09/2026 a direct HTTP call with
/// params.dryRun=true committed four wall attachments because only the query
/// string and envelope field were read.
/// </summary>
public class DryRunResolutionTests
{
    [Fact]
    public void No_flag_is_not_dry_run()
        => Assert.False(McpHttpServer.ResolveDryRun(null, new JsonObject { ["command"] = "x", ["params"] = new JsonObject() }));

    [Fact]
    public void Envelope_flag_is_dry_run()
        => Assert.True(McpHttpServer.ResolveDryRun(null, new JsonObject { ["dryRun"] = true }));

    [Fact]
    public void Params_flag_is_dry_run()
        => Assert.True(McpHttpServer.ResolveDryRun(null,
            new JsonObject { ["command"] = "attach_wall_top", ["params"] = new JsonObject { ["dryRun"] = true } }));

    [Fact]
    public void Query_false_does_not_override_true_body()
        => Assert.True(McpHttpServer.ResolveDryRun("false",
            new JsonObject { ["params"] = new JsonObject { ["dryRun"] = true } }));

    [Fact]
    public void String_true_is_accepted()
        => Assert.True(McpHttpServer.ResolveDryRun(null, new JsonObject { ["dryRun"] = "true" }));

    [Fact]
    public void Batch_step_flag_makes_whole_batch_dry_run()
    {
        var envelope = new JsonObject
        {
            ["steps"] = new JsonArray
            {
                new JsonObject { ["command"] = "a", ["params"] = new JsonObject() },
                new JsonObject { ["command"] = "b", ["params"] = new JsonObject { ["dryRun"] = true } },
            },
        };
        Assert.True(McpHttpServer.ResolveDryRun(null, envelope));
    }

    [Fact]
    public void Single_as_batch_step_flag_is_dry_run()
    {
        var envelope = new JsonObject
        {
            ["command"] = "batch",
            ["params"] = new JsonObject
            {
                ["steps"] = new JsonArray { new JsonObject { ["command"] = "a", ["params"] = new JsonObject { ["dryRun"] = true } } },
            },
        };
        Assert.True(McpHttpServer.ResolveDryRun(null, envelope));
    }

    [Fact]
    public void Explicit_false_everywhere_is_not_dry_run()
        => Assert.False(McpHttpServer.ResolveDryRun("false",
            new JsonObject { ["dryRun"] = false, ["params"] = new JsonObject { ["dryRun"] = false } }));
}
