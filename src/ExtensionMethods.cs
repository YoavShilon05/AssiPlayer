using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SpotifyAPI.Web;

namespace AssiSharpPlayer
{
    public static class ExtensionMethods
    {
        public static string Join(this string slicer, IEnumerable<string> arr)
        {
            string result = "";
            var i = 0;
            var enumerable = arr as string[] ?? arr.ToArray();
            foreach (var s in enumerable)
            {
                i++;
                result += s;
                if (i < enumerable.Length)
                     result += slicer;
            }

            return result;
        }

        public static T RandomChoice<T>(this IList<T> arr)
        {
            Random r = new();
            var idx = r.Next(arr.Count);
            return arr[idx];
        }

        public static async Task<FullTrack> GetFull(this SimpleTrack track)
        {
            return await SpotifyManager.GlobalClient.Tracks.Get(track.Id);
        }
        
        public static async Task<FullAlbum> GetFull(this SimpleAlbum album)
        {
            return await SpotifyManager.GlobalClient.Albums.Get(album.Id);
        }
        
        public static async Task<FullArtist> GetFull(this SimpleArtist artist)
        {
            return await SpotifyManager.GlobalClient.Artists.Get(artist.Id);
        }

        public static IEnumerable<T> Extend<T>(this IEnumerable<T> arr, IEnumerable<T> other)
        {
            var l = new List<T>(arr);
            l.AddRange(other);
            return l;
        }

        public static IEnumerable<T> Clean<T>(this IEnumerable<T> arr)
        {
            return arr.Where(item => item != null).ToList();
        }

    }
}
