using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using System.Text;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;
using DSharpPlus.Entities;
using System.Linq;
using DSharpPlus.EventArgs;
using System.IO;
using DSharpPlus;
using SpotifyAPI.Web;
using Swan;

namespace AssiSharpPlayer
{
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    [SuppressMessage("ReSharper", "CA1822")]
    public class MainCommands : BaseCommandModule
    {
        private const string BotNotConnectedMessage = "bot is not playing on your server :( 💥";

        [Command("test")]
        public async Task Test(CommandContext ctx)
        {
            await ctx.Channel.SendMessageAsync("test working");
        }

        [Command("radio")]
        public async Task Radio(CommandContext ctx)
        {
            Player player = Program.players.ContainsKey(ctx.Guild.Id) ?
                Program.players[ctx.Guild.Id] :
                new(ctx.Member.VoiceState.Channel);
            await player.PlayRadio();
            await ctx.RespondAsync("We are downloading yours songs, please wait!");
            if (!player.running) await player.Main(ctx.Channel);
        }

        [Command("track")]
        public async Task Track(CommandContext ctx, params string[] trackName)
        {
            string search = " ".Join(trackName);
            var track = await SpotifyManager.SearchSong(search);

            Player player = Program.players.ContainsKey(ctx.Guild.Id) ?
                Program.players[ctx.Guild.Id] :
                new(ctx.Member.VoiceState.Channel);
            await player.AddTrack(track, ctx.Channel);
            if (!player.running) await player.Main(ctx.Channel);
        }

        [Command("album")]
        public async Task Album(CommandContext ctx, params string[] albumName)
        {
            string search = " ".Join(albumName);
            var album = await SpotifyManager.SearchAlbum(search);

            Player player = Program.players.ContainsKey(ctx.Guild.Id) ?
                Program.players[ctx.Guild.Id] :
                new(ctx.Member.VoiceState.Channel);
            await player.AddAlbum(album, ctx.Channel);
            if (!player.running) await player.Main(ctx.Channel);
        }

        [Command("terminate")]
        public async Task Terminate(CommandContext ctx)
        {
            if (Program.players.ContainsKey(ctx.Guild.Id))
            {
                Program.players[ctx.Guild.Id].terminate = true;
                Program.players.Remove(ctx.Guild.Id);
                await ctx.RespondAsync("bot has terminated!");
            }
            else
                await ctx.RespondAsync(BotNotConnectedMessage);
        }

        [Command("kill")]
        public Task Kill(CommandContext ctx)
        {
            return Task.CompletedTask;
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

                await manager.CreateClient(code.Result.Content, "https://www.google.com/", ctx.Member.Id);
                await ctx.Channel.SendMessageAsync("thank you");

                if (SpotifyManager.Cache.ContainsKey(ctx.User.Id) && ctx.Member.VoiceState.Channel != null)
                    Program.players[ctx.Member.VoiceState.Channel.Id].SetRadio();
            }
        }

        [Command("disconnect")]
        public async Task Disconnect(CommandContext ctx)
        {
            var creds = SpotifyManager.DeserializeCreds();
            creds.Remove(ctx.Member.Id);
            await File.WriteAllTextAsync("Connections.json", creds.ToJson());
            await ctx.RespondAsync("Disconnected from the database!");

            if (SpotifyManager.Cache.ContainsKey(ctx.User.Id) && ctx.Member.VoiceState.Channel != null)
                Program.players[ctx.Member.VoiceState.Channel.Id].SetRadio();
        }

        [Command("test_connection")]
        public async Task TestConnection(CommandContext ctx)
        {
            var client = await SpotifyManager.GetClient(ctx.Member.Id);
            var tracks = await client.Personalization.GetTopTracks(new()
                {Limit = 25, TimeRangeParam = PersonalizationTopRequest.TimeRange.ShortTerm});
            await ctx.Channel.SendMessageAsync("\n".Join(tracks.Items!.Select(t => t.Name)));
        }

        [Command("skip")]
        public async Task Skip(CommandContext ctx)
        {
            if (Program.players.ContainsKey(ctx.Guild.Id))
            {
                if (!Program.players[ctx.Guild.Id].voteSkipping)
                    await Program.players[ctx.Guild.Id].Skip(ctx.Channel);
                else
                    await ctx.RespondAsync("a vote skip is already going in this server.");
            }
            else
                await ctx.RespondAsync(BotNotConnectedMessage);
        }

        [Command("queue")]
        public async Task Queue(CommandContext ctx)
        {
            if (Program.players.ContainsKey(ctx.Guild.Id))
            {
                var player = Program.players[ctx.Guild.Id];

                string result = "```";
                int i = 0;
                if (player.downloadedQueue.Count == 0)
                {
                    await ctx.RespondAsync("Queue is empty right now!");
                    return;
                }
                foreach (var track in player.downloadedQueue)
                {
                    i++;
                    result += $"{i}. {track.FullName} - {track.Track.Artists[0].Name} - {new TimeSpan(0, 0, (int)track.Length)}\n";
                }

                await ctx.RespondAsync(result + "```");
            }
            else
                await ctx.RespondAsync(BotNotConnectedMessage);
        }

        [Command("remove")]
        public async Task RemoveFromQueue(CommandContext ctx, int index)
        {
            if (Program.players.ContainsKey(ctx.Guild.Id))
            {
                var player = Program.players[ctx.Guild.Id];
                await player.RemoveFromQueue(index);
            }
            else
                await ctx.RespondAsync(BotNotConnectedMessage);
        }

        [Command("time")]
        public async Task Time(CommandContext ctx)
        {
            Console.WriteLine("is better than money.");
            if (Program.players.ContainsKey(ctx.Guild.Id))
            {
                Player p = Program.players[ctx.Guild.Id];
                TrackRecord t = p.CurrentTrack;
                if (t != null)
                {
                    await ctx.RespondAsync(
                        $"{p.songStartTime + new TimeSpan(0, 0, 0, (int)p.CurrentTrack.Length) - DateTime.Now:m\\:ss}" +
                        " minutes are left");
                }
                else
                    await ctx.RespondAsync("No song is currently playing on your server! 💥");
            }
            else
                await ctx.RespondAsync(BotNotConnectedMessage);
        }
    }

    public static class Events
    {
        public static Task Ready(object sender, ReadyEventArgs e)
        {
            Console.WriteLine("bot is ready");
            return Task.CompletedTask;
        }

        public static Task Disconnect(DiscordClient c, SocketCloseEventArgs r)
        {
            SpotifyManager.SaveCache();
            return Task.CompletedTask;
        }

        public static Task UpdateRadioOnVC(DiscordClient sender, VoiceStateUpdateEventArgs e)
        {
            if (e.Channel == null) return Task.CompletedTask;
            if (Program.players.ContainsKey(e.Guild.Id) && SpotifyManager.Cache.ContainsKey(e.User.Id))
                Program.players[e.Guild.Id].SetRadio();

            return Task.CompletedTask;
        }
    }
}
