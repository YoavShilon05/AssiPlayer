using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Text;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;
using DSharpPlus.Entities;
using System.Linq;
using DSharpPlus.EventArgs;
using System.IO;
using SpotifyAPI.Web;
using Swan;

namespace AssiSharpPlayer
{
    public class MainCommands : BaseCommandModule
    {
        [Command("test")]
        public async Task Test(CommandContext ctx)
        {
            await ctx.Channel.SendMessageAsync("test working");
        }

        [Command("radio")]
        public async Task Radio(CommandContext ctx)
        {
            Player player = Program.players.ContainsKey(ctx.Guild.Id) ? Program.players[ctx.Guild.Id] : new(ctx.Member.VoiceState.Channel);
            player.StartPlayingRadio();
            if (!player.running) await player.Main();
        }

        [Command("track")]
        public async Task Track(CommandContext ctx, params string[] track_name)
        {
            string search = " ".Join(track_name);
            var track =
                await SpotifyManager.SearchSong(search);
            
            Player player = Program.players.ContainsKey(ctx.Guild.Id) ? Program.players[ctx.Guild.Id] : new(ctx.Member.VoiceState.Channel);
            await player.AddTrack(track);
            if (!player.running) await player.Main();

        }

        [Command("album")]
        public async Task Album(CommandContext ctx, params string[] album_name)
        {
            string search = " ".Join(album_name);
            var album = await SpotifyManager.SearchAlbum(search);
            
            Player player = Program.players.ContainsKey(ctx.Guild.Id) ? Program.players[ctx.Guild.Id] : new(ctx.Member.VoiceState.Channel);
            await player.PlayAlbum(album);
        }
        
        [Command("terminate")]
        public async Task Terminate(CommandContext ctx)
        {
            if (Program.players.ContainsKey(ctx.Guild.Id))
            {
                Program.players[ctx.Guild.Id].terminate = true;
                Program.players.Remove(ctx.Guild.Id);
            }
            else
                await ctx.RespondAsync("bot is not playing on your server :(");
        }
        
        [Command("kill")]
        public async Task Kill(CommandContext ctx)
        {
            
        }
        
        [Command("connect")]
        public async Task Connect(CommandContext ctx)
        {
            if (SpotifyManager.DeserializeCreds().ContainsKey(ctx.Member.Id))
                await ctx.RespondAsync("you are already connected!");
            else
            {
                SpotifyManager manager = new();
                string url = manager.GetConnectionURL("https://www.google.com/");
                await ctx.Channel.SendMessageAsync(
                    $"enter this link > {url}\n and write back the address of the site you were redirected to.");

                var code = await Program.Bot.Interactivity.WaitForMessageAsync(m =>
                    m.Author.Id == ctx.Member.Id && m.Channel.Id == ctx.Channel.Id);

                var client = await manager.CreateClient(code.Result.Content, "https://www.google.com/", ctx.Member.Id);
                await ctx.Channel.SendMessageAsync("thank you");
            }
        }
        
        [Command("disconnect")]
        public async Task Disconnect(CommandContext ctx)
        {
            var creds = SpotifyManager.DeserializeCreds();
            creds.Remove(ctx.Member.Id);
            await File.WriteAllTextAsync("Connections.json", creds.ToJson());
        }
        
        [Command("test_connection")]
        public async Task TestConnection(CommandContext ctx)
        {
            var client = await SpotifyManager.GetClient(ctx.Member.Id);
            var tracks = await client.Personalization.GetTopTracks(new(){Limit=25, TimeRangeParam = PersonalizationTopRequest.TimeRange.ShortTerm});
            await ctx.Channel.SendMessageAsync("\n".Join(tracks.Items!.Select(t => t.Name)));
        }
        
        [Command("skip")]
        public async Task Skip(CommandContext ctx)
        {
            if (Program.players.ContainsKey(ctx.Guild.Id))
                Program.players[ctx.Guild.Id].skip = true;
            else
                await ctx.RespondAsync("bot is not playing on your server :(");
        }
        
        [Command("queue")]
        public async Task Queue(CommandContext ctx)
        {
            string PrintQueue(Queue<TrackRecord> queue)
            {
                string result = "```";
                foreach (var track in queue)
                {
                    result += $"{track.FullName} - {track.Channel}\n";
                }
                return result + "```";
            }
            
            
            if (Program.players.ContainsKey(ctx.Guild.Id))
            {
                var player = Program.players[ctx.Guild.Id];
                if (player.songQueue.Count > 0) await ctx.RespondAsync(PrintQueue(player.songQueue));
                else await ctx.RespondAsync(PrintQueue(player.radioQueue));
            }
            else
                await ctx.RespondAsync("bot is not playing on your server :(");
        }
    }

    static class Events
    {
        public static async Task Ready(object sender, ReadyEventArgs e)
        {
            Console.WriteLine("bot is ready");
        }
    }
}
