using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Muwbta.Mcp;

// The MCP server for the builder API (docs/PAT-AND-MCP.md). Reads with any token; writes with a
// BuilderWrite one, and never into a world the running game is serving unless --allow-active says
// so. Two guards, in two places on purpose: the scope is the server's and cannot be argued with
// from here, and WorldGuard is this process's, because "which world is live" is a question only
// the caller's intent can settle.

BuilderOptions options;

try
{
    options = BuilderOptions.FromEnvironment(args);
}
catch (InvalidOperationException ex)
{
    Console.Error.WriteLine(ex.Message);
    Console.Error.WriteLine();
    Console.Error.WriteLine(BuilderOptions.Usage);
    return 2;
}

var builder = Host.CreateApplicationBuilder(args);

// Stdio is the protocol here: anything written to stdout that is not a JSON-RPC message corrupts
// the session, and the failure looks like the agent silently not seeing the server. Logging goes
// to stderr for that reason and no other.
builder.Logging.ClearProviders();
builder.Logging.AddConsole(console => console.LogToStandardErrorThreshold = LogLevel.Trace);

builder.Services.AddSingleton(options);
builder.Services.AddHttpClient<BuilderClient>();
builder.Services.AddSingleton<WorldGuard>();

builder.Services
    .AddMcpServer(server => server.ServerInstructions = ServerGuidance.Instructions)
    .WithStdioServerTransport()
    .WithToolsFromAssembly()
    .WithResourcesFromAssembly();

await builder.Build().RunAsync().ConfigureAwait(false);
return 0;
