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
        public Queue<TrackRecord> radioQueue = new();
        public Queue<TrackRecord> songQueue = new();
        public List<TrackRecord> history = new();
        private VoiceNextConnection voiceConnection;
        private const int QueueLength = 3;
        private const float VoteSkipsPrecent = 0.33333333f;

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
                    if (r.Emoji.GetDiscordName() == ":white_check_mark:") 
                        return (MathF.Ceiling(r.Count / (float)GetMembersListening().Count()) >= VoteSkipsPrecent);
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

        private async Task SendTrackEmbed(TrackRecord track, DiscordChannel channel)
        {
            var author = new DiscordEmbedBuilder.EmbedAuthor();
            author.Name = track.Track.Artists[0].Name;
            author.Url = track.Track.Artists[0].Href;
            DiscordEmbedBuilder embed = new()
            {
                Title = track.FullName.Replace(".mp4", "").Replace(".mp3", ""),
                ImageUrl = track.Track.Album.Images[0].Url,
                Author = author
            };
            embed.AddField($"Spotify link", $"[Spotify Url]({track.Track.ExternalUrls["spotify"]})", true);
            embed.AddField($"Youtube link", $"[Youtube Url]({track.Uri})", true);
            embed.AddField("Duration", $"{track.Length / 60} : {track.Length % 60}");

            await channel.SendMessageAsync(embed);
        }
        
        private async Task<FullTrack> Radio() =>
            await RadioSongGenerator.RandomFavorite(GetMembersListening(), history, radioQueue);

        private async Task Download(FullTrack track, Queue<TrackRecord> queue, bool ordered=false)
        {
            TrackRecord hollowRecord = null;
            if (ordered)
            {
                hollowRecord = new TrackRecord(null, null, null, 0, null,null, null, false);
                queue.Enqueue(hollowRecord);
            }

            TrackRecord record = await Downloader.SearchAudio(track);
            if (ordered)
                hollowRecord.Set(record);
            else queue.Enqueue(record);
            
            Console.WriteLine($"Downloaded track {track.Name}");
        }

        public async Task AddTrack(FullTrack track)
        {
            await Download(track, songQueue, true);
        }
        
        public async Task PlayAlbum(FullAlbum album, DiscordChannel channel)
        {
            List<SimpleTrack> tracks = album.Tracks.Items;
            Thread[] downloaders = new Thread[tracks!.Count];
            await AddTrack(await tracks[0].GetFull());
            
            if (!running) await Main(channel);
            
            for (int i = 1; i <= tracks.Count; i++)
            {
                var j = i;
                downloaders[i] = new Thread(async () => await AddTrack(await tracks[j].GetFull()));
                downloaders[i].Start();
                await Task.Delay(500);
            }
        }

        public void StartPlayingRadio()
        {
            playingRadio = true;

            for (int i = 0; i < QueueLength- 1; i++)
            {
                Thread th = new(async () => await Download(await Radio(), radioQueue));
                th.Start();
            }
        }
        
        public async Task Main(DiscordChannel channel)
        {
            await channel.SendMessageAsync("Just a minute, we're downloading the songs!");
            
            running = true;

            while (true)
            {
                Thread downloader = null;
                if (songQueue.Count == 0 && playingRadio)
                {
                    FullTrack nextSong = await Radio();
                    downloader = new(async () => await Download(nextSong, radioQueue));
                    if (radioQueue.Count == 0) await Download(nextSong, radioQueue);
                    else downloader.Start();
                }

                TrackRecord song;
                if (songQueue.Count > 0)
                {
                    while (!songQueue.Peek().Downloaded) await Task.Delay(1);
                    song = songQueue.Dequeue();
                }
                else if (playingRadio)
                {
                    while (radioQueue.Count == 0) await Task.Delay(1);
                    song = radioQueue.Dequeue();
                }
                else break;

                await SendTrackEmbed(song, channel);
                await Play(song.Path);
                //delete last song
                if (history.Count > 0) File.Delete(history[^1].Path);
                
                history.Add(song);
                skip = false;
                
                
                if (downloader != null && downloader.IsAlive) downloader.Join();

                if (terminate)
                {
                    voiceConnection.Disconnect();
                    break;
                }
            }
            
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
            var ffout = ffmpeg!.StandardOutput.BaseStream;

            var buff = new byte[3840];
            int br;
            while ((br = await ffout.ReadAsync(buff.AsMemory(0, buff.Length))) > 0)
            {
                if (skip || terminate) break;
                
                if (br < buff.Length) // not a full sample, mute the rest
                    for (var i = br; i < buff.Length; i++)
                        buff[i] = 0;
                
                await voiceConnection.GetTransmitSink().WriteAsync(buff);
            }
            
            await voiceConnection.SendSpeakingAsync(false); // we're not speaking anymore
            await voiceConnection.WaitForPlaybackFinishAsync();
        }
    }
}
