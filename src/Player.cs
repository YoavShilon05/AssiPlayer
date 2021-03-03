using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DSharpPlus.CommandsNext;
using System.Reflection;
using System.Runtime.Intrinsics.Arm;
using System.Threading;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using DSharpPlus.VoiceNext;
using SpotifyAPI.Web;
using DSharpPlus.VoiceNext.Entities;
using DSharpPlus.Lavalink;

namespace AssiSharpPlayer
{
    public enum PlayerStates
    {
        Idle,
        Radio,
        Track,
        Album,
        Playlist
    }

    public class Player
    {
        public Queue<TrackRecord> RecordQueue = new();
        private Queue<FullTrack> TrackQueue = new();
        public List<TrackRecord> history = new();
        private VoiceNextConnection voiceConnection;
        private const int RadioQueueLength = 3;
        private const int DownloaderThreads = 1;
        private const float VoteSkipsPrecent = 0.33333333f;

        private int currentDownloader = 0;

        public bool terminate = false;
        private bool skip = false;
        public bool voteskipping = false;

        public bool running = false;
        private bool playingRadio = false;


        public Player(DiscordChannel vc)
        {
            Program.players.Add(vc.Guild.Id, this);
            voiceConnection = vc.ConnectAsync().GetAwaiter().GetResult();
        }

        private IEnumerable<DiscordMember> GetMembersListening()
        {
            var users = voiceConnection.TargetChannel.Users;
            foreach (var u in users)
                if (!u.VoiceState.IsSelfDeafened && !u.IsBot)
                    yield return u;
        }

        public async Task Skip(DiscordChannel channel)
        {
            voteskipping = true;
            var msg = await channel.SendMessageAsync("all in favor of skipping vote");
            await msg.CreateReactionAsync(DiscordEmoji.FromName(Program.Bot.Client, ":white_check_mark:"));

            bool SkipVote(MessageReactionAddEventArgs m)
            {
                var reactions = m.Message.Reactions;
                foreach (var r in reactions)
                {
                    if (r.Emoji.GetDiscordName() == ":white_check_mark:")
                        return MathF.Ceiling((r.Count - 1) / (float)GetMembersListening().Count()) >= VoteSkipsPrecent;
                }

                throw new Exception("Could not find reaction \":white_check_mark:\" in message reactions");
            }

            var result = await Program.Bot.Interactivity.WaitForReactionAsync(SkipVote, new TimeSpan(0, 1, 0));
            voteskipping = false;
            if (!result.TimedOut)
            {
                skip = true;
                await channel.SendMessageAsync("Skipping...");
            }
            else await channel.SendMessageAsync("Vote skip timeout");
        }

        private static async Task SendTrackEmbed(TrackRecord track, DiscordChannel channel)
        {
            var author = new DiscordEmbedBuilder.EmbedAuthor
            {
                Name = track.Track.Artists[0].Name,
                Url = track.Track.Artists[0].ExternalUrls["spotify"]
            };
            DiscordEmbedBuilder embed = new()
            {
                Title = track.FullName.Replace(".mp4", "").Replace(".mp3", ""),
                ImageUrl = track.Track.Album.Images[0].Url,
                Author = author
            };
            embed.AddField("Links", $"[Spotify]({track.Track.ExternalUrls["spotify"]}) [Youtube]({track.Uri})", true);
            embed.AddField("Duration", new TimeSpan(0, 0, (int)track.Length).ToString());

            await channel.SendMessageAsync(embed);
        }

        private async Task<FullTrack> Radio() =>
            await RadioSongGenerator.RandomFavorite(GetMembersListening(), history, RecordQueue);

        private async Task<TrackRecord> Download()
        {
            var track = TrackQueue.Dequeue();
            currentDownloader = (currentDownloader + 1) % DownloaderThreads;
            TrackRecord record = await Downloader.SearchAudio(track);
            RecordQueue.Enqueue(record);
            Console.WriteLine($"Downloaded track {track.Name}");
            return record;
        }

        public async Task AddTrack(FullTrack track, DiscordChannel channel)
        {
            TrackQueue.Enqueue(track);
            await channel.SendMessageAsync($"Downloading Track {track.Name} - {track.Artists[0].Name}");
        }

        public async Task AddAlbum(FullAlbum album, DiscordChannel channel)
        {
            List<SimpleTrack> tracks = album.Tracks.Items;
            foreach (var t in tracks!) 
                TrackQueue.Enqueue(await t.GetFull());
            await channel.SendMessageAsync($"Downloading Track {album.Name} - {album.Artists[0].Name}");
        }

        public async Task PlayRadio()
        {
            playingRadio = true;

            for (int i = 0; i < RadioQueueLength; i++)
                TrackQueue.Enqueue(await Radio());
        }

        public Queue<FullTrack> GetFullQueue()
        {
            Queue<FullTrack> songQueue = new(); 
            foreach (var v in RecordQueue)
                songQueue.Enqueue(v.Track);
                
            foreach (var t in TrackQueue)
                songQueue.Enqueue(t);

            return songQueue;
        }

        private async Task DownloaderThread(int index)
        {
            while (!terminate)
            {
                while (TrackQueue.Count == 0 || currentDownloader != index) await Task.Delay(1000);
                await Download();
            }
        }
        
        public async Task Main(DiscordChannel channel)
        {
            Thread[] downloaders = new Thread[DownloaderThreads];
            //start up threads
            for (int i = 0; i < DownloaderThreads; i++)
            {
                var j = i;
                Thread th = new(async () => await DownloaderThread(j));
                downloaders[j] = th;
                th.Start();
            }
            
            running = true;

            while (true)
            {
                if (GetFullQueue().Count <= RadioQueueLength && playingRadio)
                {
                    TrackQueue.Enqueue(await Radio());
                }

                while (RecordQueue.Count == 0) await Task.Delay(1000);

                TrackRecord song = RecordQueue.Dequeue();

                await SendTrackEmbed(song, channel);
                await Play(song.Path);
                //delete last song
                if (history.Count > 0) File.Delete(history[^1].Path);

                history.Add(song);
                skip = false;

                if (terminate)
                {
                    voiceConnection.Disconnect();
                    break;
                }
            }
            
            foreach (var d in downloaders) d.Join();
            running = false;
        }

        private async Task Play(string file)
        {
            if (voiceConnection == null)
                throw new InvalidOperationException("Not connected in this guild.");

            if (!File.Exists(file))
                throw new FileNotFoundException("File was not found.");

            await voiceConnection.SendSpeakingAsync(); // send a speaking indicator

            var psi = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = $@"-i ""{file}"" -ac 2 -f s16le -ar 48000 pipe:1",
                RedirectStandardOutput = true,
                UseShellExecute = false
            };
            var ffmpeg = Process.Start(psi);
            await using var ffout = ffmpeg!.StandardOutput.BaseStream;

            var buff = new byte[3840];
            int br;
            while ((br = await ffout.ReadAsync(buff.AsMemory(0, buff.Length))) > 0)
            {
                if (skip || terminate) break;

                if (br < buff.Length) // not a full sample, mute the rest
                {
                    for (var i = br; i < buff.Length; i++)
                        buff[i] = 0;
                }

                await voiceConnection.GetTransmitSink().WriteAsync(buff);
            }

            await voiceConnection.SendSpeakingAsync(false); // we're not speaking anymore
            await voiceConnection.WaitForPlaybackFinishAsync();
        }
    }
}
