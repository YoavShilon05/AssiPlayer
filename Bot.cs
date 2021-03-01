using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using DSharpPlus;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;
using DSharpPlus.Interactivity;
using DSharpPlus.Interactivity.Extensions;
using DSharpPlus.VoiceNext;

namespace AssiSharpPlayer
{
    class Bot
    {
        public DiscordClient Client { get; private set; }
        public CommandsNextExtension Commands { get; private set; }
        public InteractivityExtension Interactivity { get; private set; }
        public VoiceNextExtension Voice { get; private set; }
        
        public string[] Prefixes { get; private set; }

        public Bot(string[] prefixes)
        {
            Prefixes = prefixes;
        }

        public async Task Run(string token)
        {

            var botConfig = new DiscordConfiguration
            {
                Token = token,
            };

            Client = new DiscordClient(botConfig);

            var commandsConfig = new CommandsNextConfiguration
            {
                StringPrefixes = Prefixes
            };

            Commands = Client.UseCommandsNext(commandsConfig);
            Interactivity = Client.UseInteractivity(new InteractivityConfiguration() { Timeout = Timeout.InfiniteTimeSpan });
            Voice = Client.UseVoiceNext();
            
            await Client.ConnectAsync();

            //register commands 
            Commands.RegisterCommands<MainCommands>();
            
            //register events
            Client.Ready += Events.Ready;

            //Extend bot duration Indefinitely.
            await Task.Delay(-1);

        }
    }
}
