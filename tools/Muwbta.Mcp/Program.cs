using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Muwbta.Mcp;

// Phase A of docs/PAT-AND-MCP.md: read-only tools over the builder API, authenticated by a session
// cookie the operator pastes in. Nothing in src/ changes, and nothing here can write - the client
// issues GET and only GET (see BuilderClient), so the question this phase exists to answer can be
// asked without any of the credential work in Part 1 of that document.

BuilderOptions options;

try
{
    options = BuilderOptions.FromEnvironment();
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

builder.Services
    .AddMcpServer(server => server.ServerInstructions = ServerGuidance.Instructions)
    .WithStdioServerTransport()
    .WithToolsFromAssembly()
    .WithResourcesFromAssembly();

await builder.Build().RunAsync().ConfigureAwait(false);
return 0;
