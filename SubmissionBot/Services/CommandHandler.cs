using System.Reflection;
using Discord;
using Discord.Commands;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;

namespace SubmissionBot.Services;

public class CommandHandler
{
    private readonly DiscordSocketClient _client;
    private readonly InteractionService _interactionCommands;
    private readonly CommandService _textCommands;
    private readonly IServiceProvider _services;
    private readonly IConfiguration _config;

    public CommandHandler(DiscordSocketClient client, CommandService textCommands,
        InteractionService interactionCommands, IServiceProvider services, IConfiguration config)
    {
        _client = client;
        _interactionCommands = interactionCommands;
        _services = services;
        _textCommands = textCommands;
        _config = config;
    }

    public async Task InitializeAsync()
    {
        // Add the public modules that inherit InteractionModuleBase<T> to the InteractionService
        await _interactionCommands.AddModulesAsync(Assembly.GetEntryAssembly(), _services);
        // Another approach to get the assembly of a specific type is:
        // typeof(CommandHandler).Assembly

        // Register modules that are public and inherit ModuleBase<T>.
        await _textCommands.AddModulesAsync(Assembly.GetEntryAssembly(), _services);


        // Process the InteractionCreated payloads to execute Interactions commands
        _client.InteractionCreated += HandleInteraction;

        // Process the command execution results
        _interactionCommands.SlashCommandExecuted += SlashInteractionCommandExecuted;
        _interactionCommands.ContextCommandExecuted += ContextInteractionCommandExecuted;
        _interactionCommands.ComponentCommandExecuted += ComponentInteractionCommandExecuted;
    }

    public async Task DeInitialiseAsync()
    {
        _client.InteractionCreated -= HandleInteraction;
        _interactionCommands.SlashCommandExecuted -= SlashInteractionCommandExecuted;
        _interactionCommands.ContextCommandExecuted -= ContextInteractionCommandExecuted;
        _interactionCommands.ComponentCommandExecuted -= ComponentInteractionCommandExecuted;
    }

    # region Interaction Error Handling

    private async Task ComponentInteractionCommandExecuted(ComponentCommandInfo arg1, Discord.IInteractionContext arg2,
        Discord.Interactions.IResult arg3)
    {
        Console.WriteLine($"{DateTime.Now}: {arg2.User.Username} executed interaction {arg1.Name}");
        
        if (!arg3.IsSuccess)
        {
            switch (arg3.Error)
            {
                case InteractionCommandError.UnmetPrecondition:
                    if (arg2 is SocketInteractionContext ctx)
                    {
                        await ctx.Interaction.RespondAsync("You are missing permissions to do this", ephemeral:true);
                    }
                    break;
                case InteractionCommandError.UnknownCommand:
                    // implement
                    break;
                case InteractionCommandError.BadArgs:
                    // implement
                    break;
                case InteractionCommandError.Exception:
                    if (arg3 is Discord.Interactions.ExecuteResult execResult)
                    {
                        if (arg2 is SocketInteractionContext ctx2)
                        {
                            await ctx2.Interaction.RespondAsync(execResult.Exception.Message, ephemeral:true);
                            return;
                        }
                    }
                    break;
                case InteractionCommandError.Unsuccessful:
                    // implement
                    break;
                default:
                    break;
            }
        }
    }

    private Task ContextInteractionCommandExecuted(ContextCommandInfo arg1, Discord.IInteractionContext arg2,
        Discord.Interactions.IResult arg3)
    {
        if (!arg3.IsSuccess)
        {
            switch (arg3.Error)
            {
                case InteractionCommandError.UnmetPrecondition:
                    // implement
                    break;
                case InteractionCommandError.UnknownCommand:
                    // implement
                    break;
                case InteractionCommandError.BadArgs:
                    // implement
                    break;
                case InteractionCommandError.Exception:
                    // implement
                    break;
                case InteractionCommandError.Unsuccessful:
                    // implement
                    break;
                default:
                    break;
            }
        }

        return Task.CompletedTask;
    }

    private async Task SlashInteractionCommandExecuted(SlashCommandInfo arg1, Discord.IInteractionContext arg2,
        Discord.Interactions.IResult arg3)
    {
        Console.WriteLine($"{DateTime.Now}: {arg2.User.Username} executed slash command {arg1.Name}");
        
        if (!arg3.IsSuccess)
        {
            switch (arg3.Error)
            {
                case InteractionCommandError.UnmetPrecondition:
                    if (arg2 is SocketInteractionContext ctx)
                    {
                        await ctx.Interaction.RespondAsync("You are missing permissions to do this", ephemeral:true);
                    }
                    // implement
                    break;
                case InteractionCommandError.UnknownCommand:
                    // implement
                    break;
                case InteractionCommandError.BadArgs:
                    // implement
                    break;
                case InteractionCommandError.Exception:
                    if (arg3 is Discord.Interactions.ExecuteResult execResult)
                    {
                        if (arg2 is SocketInteractionContext ctx2)
                        {
                            await ctx2.Interaction.RespondAsync(execResult.Exception.Message, ephemeral:true);
                            return;
                        }
                    }
                    goto default;
                case InteractionCommandError.Unsuccessful:
                    // implement
                    break;
                default:
                    break;
            }
        }
    }

    # endregion

    # region Interaction Execution

    private async Task HandleInteraction(SocketInteraction arg)
    {
        try
        {
            // Create an execution context that matches the generic type parameter of your InteractionModuleBase<T> modules
            var ctx = new SocketInteractionContext(_client, arg);
            await _interactionCommands.ExecuteCommandAsync(ctx, _services);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);

            // If a Slash Command execution fails it is most likely that the original interaction acknowledgement will persist. It is a good idea to delete the original
            // response, or at least let the user know that something went wrong during the command execution.
            if (arg.Type == InteractionType.ApplicationCommand)
                await arg.GetOriginalResponseAsync().ContinueWith(async (msg) => await msg.Result.DeleteAsync());
        }
    }

    # endregion
}