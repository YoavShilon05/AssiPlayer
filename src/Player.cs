using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DSharpPlus.Entities;
using DSharpPlus.VoiceNext;
using SpotifyAPI.Web;

namespace AssiSharpPlayer
{
    public class Player
    {
        private VoiceNextConnection voiceConnection = null;

        private QueueManager queue;
        public bool running;

        public Player(DiscordChannel voiceChannel)
        {
            voiceConnection = voiceChannel.ConnectAsync().GetAwaiter().GetResult();
            queue = new QueueManager(GetMembersListening());
        }

        public async Task PlayRadio()
        {
            for (int i = 0; i < QueueManager.RadioQueueAmount; i++)
                await queue.QueueNextRadio();
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
            while (true)
            {
                TrackRecord track = await queue.NextTrack();
                await SendTrackEmbed(track, textChannel);
                await Play(track.Path);
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

        public IEnumerable<DiscordMember> GetMembersListening()
        {
            var users = voiceConnection.TargetChannel.Users;
            foreach (var u in users)
                if (!u.VoiceState.IsSelfDeafened && !u.IsBot)
                    yield return u;
        }
    }
}
