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

        public TrackRecord(YouTubeVideo vid, FullTrack track, string path, bool downloaded=true)
        {
            Track = track;
            FullName = vid.FullName;
            Path = path;
            Length = vid.Info.LengthSeconds.Value;
            Channel = vid.Info.Author;
            Downloaded = downloaded;
        }
        public TrackRecord(FullTrack track, string fullName, string path, long length, string channel, bool downloaded=true)
        {
            Track = track;
            FullName = fullName;
            Path = path;
            Length = length;
            Channel = channel;
            Downloaded = downloaded;
        }

        public void Set(TrackRecord other)
        {
            Track = other.Track;
            FullName = other.FullName;
            Path = other.Path;
            Length = other.Length;
            Channel = other.Channel;
            Downloaded = other.Downloaded;
        }
    }
    
    
    public static class Downloader
    {
        private static YouTube youtubeDownload = YouTube.Default;

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
            string output = record.Path.Replace(".mp4", ".mp3");
            await FFMpegArguments.FromFileInput(record.Path).OutputToFile(output).ProcessAsynchronously();
            File.Delete(record.Path);
            return new(record.Track, record.FullName, output, record.Length, record.Channel);
        }

        public static async Task<TrackRecord> DownloadAudio(string url, FullTrack track) =>
            await ConvertToAudio(await DownloadVideo(url, track));

        public static async Task<TrackRecord> SearchAudio(FullTrack track) =>
            await ConvertToAudio(await SearchVideo(track));
    }
}
