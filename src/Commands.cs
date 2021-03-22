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

        private static async Task Check(CommandContext ctx, Func<Task> a)
        {
            if (Program.players.ContainsKey(ctx.Guild.Id))
                await a();
            else
                await ctx.RespondAsync(BotNotConnectedMessage);
        }

        [Command("ping")]
        public async Task Ping(CommandContext ctx) => await ctx.RespondAsync("pong");
        
        
        [Command("radio")]
        public async Task Radio(CommandContext ctx)
        {
            Player player = Program.players.ContainsKey(ctx.Guild.Id) ?
                Program.players[ctx.Guild.Id] :
                new(ctx.Member.VoiceState.Channel);

            await ctx.RespondAsync("We are downloading yours songs, please wait!");
            await player.PlayRadio();
            if (!player.running) await player.Update(ctx.Channel);
        }

        [Command("blacklist")]
        public async Task Blacklist(CommandContext ctx, params string[] search_params)
        {
            async Task BlacklistTrack(FullTrack track)
            {
                if (Program.blacklists.ContainsKey(ctx.Member.Id))
                {
                    if (Program.blacklists[ctx.Member.Id].Contains(track.Id))
                        await ctx.RespondAsync("already blacklisted this track");
                    else
                    {
                        Program.blacklists[ctx.Member.Id].Add(track.Id);
                        Program.SerialzieBlacklists();
                        await ctx.RespondAsync($"blacklisted track {track.Name}");
                    }
                    return;
                }
                Program.blacklists.Add(ctx.Member.Id, new(){track.Id});
                Program.SerialzieBlacklists();
                await ctx.RespondAsync($"blacklisted track {track.Name}");
            }

            
            if (search_params.Length == 0)
            {
                await Check(ctx, async () =>
                {
                    await BlacklistTrack(Program.players[ctx.Guild.Id].currentTrack.Track);
                });
            }
            else
            {
                string search = " ".Join(search_params);
                FullTrack track = (await SpotifyManager.GlobalClient.Search.Item(new(SearchRequest.Types.Track, search))).Tracks.Items![0];
                await BlacklistTrack(track);
            }
        }

        [Command("blacklists")]
        public async Task ShowBlacklists(CommandContext ctx)
        {
            if (Program.blacklists.ContainsKey(ctx.Member.Id))
            {
                await ctx.RespondAsync("\n".Join(Program.blacklists[ctx.Member.Id].Select(
                    t => SpotifyManager.GlobalClient.Tracks.Get(t).GetAwaiter().GetResult().Name)));
            }

            else await ctx.RespondAsync("You have no blacklists, such a great taste!");

        }

        [Command("remove_from_blacklist")]
        public async Task RemoveFromBlacklist(CommandContext ctx, params string[] search_params)
        {
            async Task RemoveTrackFromBlacklist(FullTrack track)
            {
                if (Program.blacklists.ContainsKey(ctx.Member.Id))
                {
                    if (Program.blacklists[ctx.Member.Id].Contains(track.Id))
                    {
                        Program.blacklists[ctx.Member.Id].Remove(track.Id);
                        Program.SerialzieBlacklists();
                        await ctx.RespondAsync("removed track from your blacklist");
                    }
                    else
                    {
                        await ctx.RespondAsync($"this track is not blacklisted.");
                    }
                    return;
                }

                await ctx.RespondAsync($"you have no blacklists!");
            }

            
            if (search_params.Length == 0)
            {
                await Check(ctx, async () =>
                {
                    await RemoveTrackFromBlacklist(Program.players[ctx.Guild.Id].currentTrack.Track);
                });
            }
            else
            {
                string search = " ".Join(search_params);
                FullTrack track = (await SpotifyManager.GlobalClient.Search.Item(new(SearchRequest.Types.Track, search))).Tracks.Items![0];
                await RemoveTrackFromBlacklist(track);
            }
        }


        [Command("clear_blacklist")]
        public async Task ClearBlacklist(CommandContext ctx)
        {
            if (Program.blacklists.ContainsKey(ctx.Member.Id))
            {
                Program.blacklists[ctx.Member.Id].Clear();
                await ctx.RespondAsync("cleared your blacklist.");
                return;
            }

            await ctx.RespondAsync($"you have no blacklists!");
        }
        
        [Command("track")]
        public async Task Track(CommandContext ctx, params string[] trackName)
        {
            string search = " ".Join(trackName);
        
            Player player = Program.players.ContainsKey(ctx.Guild.Id) ?
                Program.players[ctx.Guild.Id] :
                new(ctx.Member.VoiceState.Channel);
            await player.QueueTrack(search);
            if (!player.running) await player.Update(ctx.Channel);
        }
        
        [Command("album")]
        public async Task Album(CommandContext ctx, params string[] albumName)
        {
            string search = " ".Join(albumName);

            Player player = Program.players.ContainsKey(ctx.Guild.Id) ?
                Program.players[ctx.Guild.Id] :
                new(ctx.Member.VoiceState.Channel);
            await player.QueueAlbum(search);
            if (!player.running) await player.Update(ctx.Channel);
        }
        
        [Command("terminate")]
        public async Task Terminate(CommandContext ctx)
        {
            await Check(ctx, async () =>
            {
                await Program.players[ctx.Guild.Id].Terminate();
                Program.players.Remove(ctx.Guild.Id);
                await ctx.RespondAsync("bot has terminated!");
            });
        }
        //
        // [Command("kill")]
        // public Task Kill(CommandContext ctx)
        // {
        //     return Task.CompletedTask;
        // }
        //
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

                // if (SpotifyManager.Cache.ContainsKey(ctx.User.Id) && ctx.Member.VoiceState.Channel != null)
                //     Program.players[ctx.Member.VoiceState.Channel.Id].SetRadio();
            }
        }
        
        [Command("disconnect")]
        public async Task Disconnect(CommandContext ctx)
        {
            var creds = SpotifyManager.DeserializeCreds();
            creds.Remove(ctx.Member.Id);
            await File.WriteAllTextAsync("Connections.json", creds.ToJson());
            await ctx.RespondAsync("Disconnected from the database!");
            
            //TODO: ...
            //if (SpotifyManager.Cache.ContainsKey(ctx.User.Id) && ctx.Member.VoiceState.Channel != null)
            //    Program.players[ctx.Member.VoiceState.Channel.Id].SetRadio();
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
            await Check(ctx, async () =>
            {
                if (!Program.players[ctx.Guild.Id].voteskipping)
                    await Program.players[ctx.Guild.Id].VoteSkip(ctx.Channel);
                else
                    await ctx.RespondAsync("a vote skip is already going in this server.");
            });
        }

        [Command("loop")]
        public async Task Loop(CommandContext ctx, params string[] songName)
        {
            await Check(ctx, async () =>
            {
                var player = Program.players[ctx.Guild.Id];
                if (songName.Length > 0)
                {
                    string search = " ".Join(songName);
                    FullTrack t = await SpotifyManager.SearchTrack(search);
                    await player.LoopTrack(t);
                }

                else
                {
                    if (player.currentTrack != null)
                        await player.LoopTrack(player.currentTrack.Track);
                }
            });
        }
        
        [Command("pause")]
        public async Task Pause(CommandContext ctx)
        {
            await Check(ctx, async () =>
            {
                if (!Program.players[ctx.Guild.Id].pause)
                    Program.players[ctx.Guild.Id].pause = true;
                else
                    await ctx.RespondAsync("player is already paused on this server.");
            });
        }
        
        
        [Command("resume")]
        public async Task Resume(CommandContext ctx)
        {
            await Check(ctx, async () =>
            {
                if (Program.players[ctx.Guild.Id].pause)
                    Program.players[ctx.Guild.Id].pause = false;
                else
                    await ctx.RespondAsync("player is already playing on this server.");
            });
        }
        
        
        [Command("queue")]
        public async Task Queue(CommandContext ctx)
        {
            await Check(ctx, async () =>
            {
                var player = Program.players[ctx.Guild.Id];
        
                string result = "```";
                int i = 0;
                if (player.GetQueue().Count == 0)
                {
                    await ctx.RespondAsync("Queue is empty right now!");
                    return;
                }
        
                foreach (var track in player.GetQueue())
                {
                    i++;
                    result +=
                        $"{i}. {track.Name} - {track.Artists[0].Name}\n";
                }
        
                await ctx.RespondAsync(result + "```");
            });
        }
        
        [Command("remove")]
        public async Task RemoveFromQueue(CommandContext ctx, int index)
        {
            await Check(ctx, () =>
            {
                var player = Program.players[ctx.Guild.Id];
                player.RemoveFromQueue(index - 1);
                return Task.CompletedTask;
            });
        }
        
        
        [Command("time")]
        public async Task Time(CommandContext ctx)
        {
            Console.WriteLine("is better than money.");
            await Check(ctx, async () =>
            {
                Player p = Program.players[ctx.Guild.Id];
                TrackRecord t = p.currentTrack;
                if (t != null)
                {
                    await ctx.RespondAsync(
                        $"{p.currentTrackStartTime + new TimeSpan(0, 0, 0, (int)p.currentTrack.Length) - DateTime.Now:m\\:ss}" +
                        " minutes are left");
                }
                else
                    await ctx.RespondAsync("No search is currently playing on your server! 💥");
            });
        }
    }

    public class NotSoSeriousCommands : BaseCommandModule
    {
        [Command("money")]
        public async Task Money(CommandContext ctx)
        {
            await ctx.RespondAsync("is so much better than time");
        }

        private const int MaybeHomoThresh = 700;
        private const int YesHomoThresh = 1000;
        
        [Command("homo")] 
        public async Task Homo(CommandContext ctx)
        {
            Random r = new();
            int n = r.Next(1000);

            await ctx.RespondAsync(n switch
            {
                >= YesHomoThresh => "yes homo",
                >= MaybeHomoThresh => "maybe homo",
                _ => "no homo"
            });
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
            // if (e.Channel == null) return Task.CompletedTask;
            // if (Program.players.ContainsKey(e.Guild.Id) && SpotifyManager.Cache.ContainsKey(e.User.Id))
            //     Program.players[e.Guild.Id].SetRadio();

            return Task.CompletedTask;
        }
    }
}
