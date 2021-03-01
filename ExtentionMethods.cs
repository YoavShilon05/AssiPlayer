﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SpotifyAPI.Web;

namespace AssiSharpPlayer
{
    public static class ExtentionMethods
    {
        public static string Join(this string slicer, IEnumerable<string> arr)
        {
            string result = "";
            var i = 0;
            foreach (var s in arr)
            {
                i++;
                result += s;
                if (i < arr.Count())
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

    }
}