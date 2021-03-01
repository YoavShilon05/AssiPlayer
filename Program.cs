using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DSharpPlus;

namespace AssiSharpPlayer
{
    static class Program
    {
        public static Dictionary<ulong, Player> players = new();
        public static Bot Bot;
        
        
        private static async Task Main(string[] args)
        {

            Bot = new(new[] {"s."});
            Bot.Run("ODExMTI1MDUwNDgwNjU2NDQ0.YCtpEg.jv7vOVfZ3ycJaE5NBGEeXAzE8aU").GetAwaiter().GetResult();
        }
    }
}