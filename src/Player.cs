using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using DSharpPlus.VoiceNext;
using SpotifyAPI.Web;

namespace AssiSharpPlayer
{
    public class Player
    {
        
        private VoiceNextConnection voiceConnection;
        private QueueManager queue;
        public bool running;
        private bool skip;
        public bool pause = false;
        private RadioGetters radiogetter;
        
        public bool voteskipping { get; private set; }
        public const float VoteSkipsPrecent = 1 / 3f;
        public TrackRecord currentTrack;
        public DateTime currentTrackStartTime;

        private (List<FullTrack> tracks, List<FullTrack> radios) GetTracksInQueues()
        {
            List<FullTrack> tracks = new();
            List<FullTrack> radios = new();
            
            tracks.AddRange(queue.trackQueue.ToArray().Select(i => i.Track));
            radios.AddRange(queue.radioQueue.ToArray().Select(i => i.Track));

            foreach (var (track, queueType) in queue.downloading)
            {
                switch (queueType)
                {
                    case QueueType.Radio:
                        radios.Add(track);
                        break;
                    case QueueType.Track:
                        tracks.Add(track);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }

            return (tracks, radios);
        }
        
        public List<FullTrack> GetQueue()
        {
            var (result, radios) = GetTracksInQueues();
            result.AddRange(radios);
            return result;
        }
        
        public async Task QueueTrack(string search) =>
            await queue.QueueTrack(await SpotifyManager.SearchTrack(search));
        
        
        public async Task QueueAlbum(string search) =>
            await queue.QueueAlbum(await SpotifyManager.SearchAlbum(search));
        
        
        public void RemoveFromQueue(int idx)
        {
            var (tracks, radios) = GetTracksInQueues();

            if (idx < tracks.Count)
            {
                if (idx <= queue.trackQueue.Count)
                    queue.trackQueue.Remove(idx);
                else queue.RemoveItemFromDownloadingQueue(tracks[idx]);
            }
            else if (idx < tracks.Count + radios.Count)
            {
                if (idx < queue.radioQueue.Count)
                    queue.radioQueue.Remove(idx);
                else queue.RemoveItemFromDownloadingQueue(radios[idx]);
            }
            else throw new ArgumentOutOfRangeException(nameof(idx), "index given was outside the range of tracks.");
        }
        
        public Player(DiscordChannel voiceChannel)
        {
            Program.players.Add(voiceChannel.GuildId, this);
            voiceConnection = voiceChannel.ConnectAsync().GetAwaiter().GetResult();
            queue = new QueueManager(GetMembersListening().Where(u => SpotifyManager.Cache.ContainsKey(u.Id)));
        }

        public async Task PlayRadio(RadioGetters getter=RadioGetters.Radio)
        {
            queue.Clear();
            radiogetter = getter;
            for (int i = 0; i < QueueManager.RadioQueueAmount; i++)
            {
                await queue.QueueNextRadio(getter).ConfigureAwait(false);
                while (!queue.radio.set) {}
            }
        }

        public async Task VoteSkip(DiscordChannel channel)
        {
            voteskipping = true;
            //autoskip
            if (GetMembersListening().Count() * VoteSkipsPrecent <= 1)
            {
                skip = true;
                await channel.SendMessageAsync("Skipping...");
                voteskipping = false;
                return;
            }

            var msg = await channel.SendMessageAsync("all in favor of skipping vote");
            await msg.CreateReactionAsync(DiscordEmoji.FromName(Program.Bot.Client, ":white_check_mark:"));

            bool SkipVote(MessageReactionAddEventArgs m)
            {
                var reactions = m.Message.Reactions;
                foreach (var r in reactions)
                {
                    if (r.Emoji.GetDiscordName() == ":white_check_mark:")
                        return r.Count * VoteSkipsPrecent <= 1;
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

        private static async Task SendTrackEmbed(TrackRecord track, DiscordChannel textChannel)
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

            await textChannel.SendMessageAsync(embed);
        }

        public async Task Update(DiscordChannel textChannel)
        {
            running = true;
            while (running)
            {
                currentTrack = await queue.NextTrack(true, radiogetter);
                await SendTrackEmbed(currentTrack, textChannel);
                currentTrackStartTime = DateTime.Now;
                await Play(currentTrack.Path);
                skip = false;
            }
        }

        private async Task Play(string file)
        {
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
                if (skip || !running) break;
                while (pause) {}
                
                if (br < buff.Length) // not a full sample, mute the rest
                {
                    for (var i = br; i < buff.Length; i++)
                        buff[i] = 0;
                }

                await voiceConnection.GetTransmitSink().WriteAsync(buff);
            }
            ffmpeg.Kill();
            await voiceConnection.SendSpeakingAsync(false); // we're not speaking anymore
            await voiceConnection.WaitForPlaybackFinishAsync();
        }

        public async Task LoopTrack(FullTrack track)
        {
            queue.LoopTrack = track;
            await PlayRadio(RadioGetters.Loop);
        }
        
        public async Task Terminate()
        {
            running = false;
            queue.Terminate();
            voiceConnection.Disconnect();
        }
        public IEnumerable<DiscordMember> GetMembersListening()
        {
            var users = voiceConnection.TargetChannel.Users;
            foreach (var u in users)
                if (!u.VoiceState.IsSelfDeafened && !u.IsBot)
                    yield return u;
        }
    }
}
