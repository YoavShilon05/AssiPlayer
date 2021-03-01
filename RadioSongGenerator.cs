using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DSharpPlus.Entities;
using SpotifyAPI.Web;

namespace AssiSharpPlayer
{
    public class RadioSongGenerator
    {
        public static async Task<FullTrack> RandomFavorite(IEnumerable<DiscordMember> members_listening, List<TrackRecord> history, Queue<TrackRecord> queue)
        {
            IEnumerable<SpotifyClient> clients = members_listening.Select(m => SpotifyManager.GetClient(m.Id).GetAwaiter().GetResult());
            
            List<FullTrack> tracks = new();
            foreach (var client in clients)
                if (client != null)
                    tracks.AddRange(
                        (await client.Personalization.GetTopTracks(new()
                            {Limit = 50})).Items ??
                        throw new ArgumentNullException("client top tracks returned null"));

            FullTrack result = null;
            while (result == null || history.Select(r => r.Track).Contains(result) || queue.Select(r => r.Track).Contains(result))
                result = tracks.RandomChoice();
            
            return result;
        }
        
        public static string ByRating()
        {
            return null;
        }
    }
}