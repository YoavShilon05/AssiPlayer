using System;
using System.IO;
using System.Threading.Tasks;
using FFMpegCore;
using VideoLibrary;
using System.Diagnostics;
using SpotifyAPI.Web;


namespace AssiSharpPlayer
{
    public class TrackRecord
    {
        public FullTrack Track { get; private set; }
        public string FullName { get; private set; }
        public string Path { get; private set; }
        public long Length { get; private set; }
        public string Channel { get; private set; }
        public bool Downloaded { get; private set; }
        public string Thumbnail { get; private set; }
        public string Uri { get; private set; }

        public TrackRecord(YouTubeVideo vid, FullTrack track, string path, bool downloaded=true)
        {
            Track = track;
            FullName = vid.FullName;
            Path = path;
            Thumbnail = null;//vid.Thumbnail;
            Length = vid.Info.LengthSeconds!.Value;
            Channel = vid.Info.Author;
            Downloaded = downloaded;
            Uri = vid.Uri;
        }

        public TrackRecord(FullTrack track, string fullName, string path, long length, string channel, string thumbnail, string uri, bool downloaded=true)
        {
            Track = track;
            FullName = fullName;
            Path = path;
            Length = length;
            Channel = channel;
            Thumbnail = thumbnail;
            Uri = uri;
            Downloaded = downloaded;
        }
    }


    public static class Downloader
    {
        private static YouTube youtubeDownload = YouTube.Default;
        private static bool running = false;

        private static string ModifyFileName(string name)
        {
            string newName = "";

            foreach (char c in name)
                if (char.IsDigit(c) || char.IsLetter(c) || char.IsWhiteSpace(c) || c == '-' || c == '_' || c == '.')
                    newName += c;

            return newName;
        }

        public static async Task<TrackRecord> DownloadVideo(string url, FullTrack track)
        {
            Console.WriteLine($"Downloading {track.Name}");
            YouTubeVideo vid = await youtubeDownload.GetVideoAsync(url);
            string dir = ModifyFileName(vid.FullName);
            await File.WriteAllBytesAsync(dir, await vid.GetBytesAsync());
            return new(vid, track, dir);
        }

        public static async Task<TrackRecord> SearchVideo(FullTrack track)
        {
            string uri;

            var info = new ProcessStartInfo
            {
                FileName = "py",
                Arguments = "-c \"import ytmusicapi as ytmusic;" +
                            "import sys;" +
                            "yt = ytmusic.YTMusic();" +
                            "print(yt.search(sys.argv[1], 'songs', 1)[0]['videoId'])\"" +
                            $" \"{track.Name} - {track.Album.Name} - {track.Artists[0].Name}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true
            };
            using (var process = Process.Start(info))
            {
                using (StreamReader reader = process!.StandardOutput)
                    uri = await reader.ReadToEndAsync();
            }

            return await DownloadVideo($"https://www.youtube.com/watch?v={uri}", track);
        }

        private static async Task<TrackRecord> ConvertToAudio(TrackRecord record)
        {
            while (running) await Task.Delay(1); // i want to kill myself very very badly.
            running = true;
            string output = record.Path.Replace(".mp4", ".mp3");
            await FFMpegArguments.FromFileInput(record.Path).OutputToFile(output).ProcessAsynchronously();
            File.Delete(record.Path);
            running = false;
            return new(record.Track, record.FullName, output, record.Length, record.Channel, record.Thumbnail, record.Uri);
        }

        public static async Task<TrackRecord> DownloadAudio(string url, FullTrack track) =>
            await ConvertToAudio(await DownloadVideo(url, track));

        public static async Task<TrackRecord> SearchAudio(FullTrack track) =>
            await ConvertToAudio(await SearchVideo(track));
    }
}
