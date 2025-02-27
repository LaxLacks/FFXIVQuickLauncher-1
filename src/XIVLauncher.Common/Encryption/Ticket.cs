﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Serilog;
using XIVLauncher.Common.Encryption.BlockCipher;
using XIVLauncher.Common.PlatformAbstractions;

namespace XIVLauncher.Common.Encryption;

public class Ticket
{
    public string Text { get; }
    public int Length { get; }

    private const string FUCKED_GARBAGE_ALPHABET = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz-_";

    private Ticket(string text, int length)
    {
        this.Text = text;
        this.Length = length;
    }

    public static async Task<Ticket?> Get(ISteam steam)
    {
        var ticketBytes = await steam.GetAuthSessionTicketAsync().ConfigureAwait(true);

        if (ticketBytes == null)
            return null;

        var time = 60 * ((steam.GetServerRealTime() - 5) / 60);

        var ticketString = BitConverter.ToString(ticketBytes).Replace("-", "").ToLower();
        var rawTicketBytes = Encoding.ASCII.GetBytes(ticketString);

        var rawTicket = new byte[rawTicketBytes.Length + 1];
        Array.Copy(rawTicketBytes, rawTicket, rawTicketBytes.Length);
        rawTicket[rawTicket.Length - 1] = 0;

        Log.Debug(Util.ByteArrayToHex(rawTicket));

        var blowfishKey = $"{time:x08}#un@e=x>";

        using var memorySteam = new MemoryStream();
        using var binaryWriter = new BinaryWriter(memorySteam);

        /* REGULAR SUM + TICKET */
        ushort ticketSum = 0;

        foreach (byte b in rawTicket)
        {
            ticketSum += b;
        }

        binaryWriter.Write(ticketSum);
        binaryWriter.Write(rawTicket);

        /* GARBAGE */
        int castTicketSum = unchecked((short)ticketSum);
        Log.Information($"{castTicketSum:X}");
        var seed = time ^ castTicketSum;
        var rand = new CrtRand((uint)seed);

        var numRandomBytes = ((ulong)(rawTicket.Length + 9) & 0xFFFFFFFFFFFFFFF8) - 2 - (ulong)rawTicket.Length;
        var garbage = new byte[numRandomBytes];

        uint fuckedSum = BitConverter.ToUInt32(memorySteam.ToArray(), 0);

        for (var i = 0u; i < numRandomBytes; i++)
        {
            var randChar = FUCKED_GARBAGE_ALPHABET[(int)(fuckedSum + rand.Next()) & 0x3F];
            garbage[i] = (byte)randChar;
            fuckedSum += randChar;
        }

        binaryWriter.Write(garbage);

        memorySteam.Seek(0, SeekOrigin.Begin);
        binaryWriter.Write(fuckedSum);

        Log.Information("[STEAM] time: {Time}, bfKey: {FishKey}, rawTicket.Length: {TicketLen}, ticketSum: {TicketSum}, fuckedSum: {FuckedSum}, seed: {Seed}, numRandomBytes: {NumRandomBytes}", time,
            blowfishKey, rawTicket.Length, ticketSum, fuckedSum, seed, numRandomBytes);

        /* ENC + SPLIT */
        var finalBytes = memorySteam.ToArray();

        var t = finalBytes[0];
        finalBytes[0] = finalBytes[1];
        finalBytes[1] = t;

        var keyBytes = Encoding.ASCII.GetBytes(blowfishKey);
        Log.Debug(Util.ByteArrayToHex(keyBytes));

        var blowfish = new Blowfish(keyBytes);
        var ecb = new Ecb<Blowfish>(blowfish);

        Log.Debug(Util.ByteArrayToHex(finalBytes));

        var encBytes = new byte[finalBytes.Length];
        Debug.Assert(encBytes.Length % 8 == 0);

        ecb.Encrypt(finalBytes, encBytes);
        var encString = Util.ToMangledSeBase64(encBytes);

        Log.Debug(Util.ByteArrayToHex(encBytes));

        const int SPLIT_SIZE = 300;
        var parts = ChunksUpto(encString, SPLIT_SIZE).ToArray();

        var finalString = string.Join(",", parts);

        return new Ticket(finalString, finalString.Length - (parts.Length - 1));
    }

    private static IEnumerable<string> ChunksUpto(string str, int maxChunkSize)
    {
        for (var i = 0; i < str.Length; i += maxChunkSize)
            yield return str.Substring(i, Math.Min(maxChunkSize, str.Length - i));
    }
}