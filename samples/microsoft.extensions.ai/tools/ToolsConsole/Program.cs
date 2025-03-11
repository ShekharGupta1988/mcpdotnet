using McpDotNet.Client;
using McpDotNet.Configuration;
using McpDotNet.Protocol.Transport;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using OpenAI;
using SimpleToolsConsole;

internal class Program
{
    private static async Task<IMcpClient> GetMcpClientAsync()
    {

        McpClientOptions options = new()
        {
            ClientInfo = new() { Name = "SimpleToolsConsole", Version = "1.0.0" }
        };

        var config = new McpServerConfig
        {
            Id = "everything",
            Name = "Everything",
            TransportType = TransportTypes.Sse,
            Location = "http://localhost:8080/sse"
        };

        var factory = new McpClientFactory(
            [config],
            options,
            NullLoggerFactory.Instance
        );

        return await factory.GetClientAsync("everything");
    }

    private static async Task Main(string[] args)
    {
        try
        {
            Console.WriteLine("Initializing MCP 'everything' server");
            var client = await GetMcpClientAsync();
            Console.WriteLine("MCP 'everything' server initialized");
            Console.WriteLine("Listing tools...");
            var tools = await client.ListToolsAsync();
            var mappedTools = tools.Tools.Select(t => t.ToAITool(client)).ToList();
            Console.WriteLine("Tools available:");
            foreach (var tool in mappedTools)
            {
                Console.WriteLine("  " + tool);
            }

            var prompts = await client.ListPromptsAsync();
            string systemPrompt = string.Empty;


            foreach (var prompt in prompts.Prompts)
            {
                var promptResult = await client.GetPromptAsync(prompt.Name);
                string promptStr = promptResult.Messages.FirstOrDefault()?.Content.Text ?? string.Empty;
                systemPrompt = $"{systemPrompt}\n{promptStr}";
            }


            Console.WriteLine("Starting chat with GPT-4o-mini...");

            // Note: We use then Microsoft.Extensions.AI.OpenAI client here, but it could be any other MEAI client.
            // Provide your own OPENAI_API_KEY via an environment variable, secret or file-based appsettings. Do not hardcode it.
            IChatClient openaiClient = new OpenAIClient(Environment.GetEnvironmentVariable("OPENAI_API_KEY"))
                .AsChatClient("gpt-4o-mini");

            // Note: To use the ChatClientBuilder you need to install the Microsoft.Extensions.AI package
            IChatClient chatClient = new ChatClientBuilder(openaiClient)
                .UseFunctionInvocation()
                .Build();

            // Create message list
            IList<Microsoft.Extensions.AI.ChatMessage> messages =
            [
                // Add a system message
                new(ChatRole.System, systemPrompt),
            ];
            // If MCP server provides instructions, add them as an additional system message (you could also add it as a content part)
            if (!string.IsNullOrEmpty(client.ServerInstructions))
            {
                messages.Add(new(ChatRole.System, client.ServerInstructions));
            }
            string? userInput;

            do
            {
                userInput = ReadUserInput();
                messages.Add(new(ChatRole.User, userInput));

                var response = chatClient.CompleteStreamingAsync(
                    messages,
                    new() { Tools = mappedTools });

                string assistantOutput = string.Empty;
                Console.Write("Assistant >>");
                await foreach (var update in response)
                {
                    assistantOutput = $"{assistantOutput}{update}";
                    Console.Write(update);
                }

                messages.Add(new(ChatRole.Assistant, assistantOutput));
                Console.WriteLine();

            } while (userInput is not null);
        }
        catch (Exception ex)
        {
            Console.WriteLine("Error occurred: " + ex.Message);
        }
    }

    private static string ReadUserInput()
    {
        Console.ForegroundColor = ConsoleColor.White;
        Console.Write("\nUser >> ");
        string userInput = Console.ReadLine();
        Console.ResetColor();

        return userInput;
    }
}