﻿using PEBakery.Lib;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PEBakery.Core
{
    /*
    [Attachment Format]
    Streams are encoded in base64 format.
    Concat all lines into one long string, append '=', '==' or nothing according to length.
    (Need '=' padding to be appended to be .Net acknowledgled base64 format)
    Decode base64 encoded string to get binary, which follows these 2 types
     
    [Type 1]
    Zlib Compressed File
    - Used in most file
    - Base64 encoded string always start with 'eJ'
    - Base64 decoded bytes always start with '78 9c' (in hex) - which is zlib stream's magic number
    
    [Type 2]
    Untouched File + Zlib Compressed Footer
    - Used in already compressed file (Ex 7z)
    - Base64 decoded footer always start with '78 9c' (in hex) - which is zlib stream's magic number
    
    Footer : 550Byte (Decompressed)
    [Length of FileName]
    [FileName]
    Stream of mostly 0 and some bytes - Maybe hash? for integrity?
    
    Fortunately, footer is not essential to extract attached file.
    Because of unknown footer, writing PEBakery's own attach format will be much better in future.

    [How to improve?]
    Use LZMA instead of zlib, for better compression rate
    */

    public class EncodedFile
    {
        #region Extract File from Plugin
        // Need more research about Type 2 and its footer
        // public static void AttachFile(Plugin plugin, string dirName, string fileName, string filePath)

        public static bool ExtractFile(Plugin plugin, string dirName, string fileName, out MemoryStream mem)
        {
            List<string> dirList = plugin.Sections["EncodedFolders"].GetLines();
            List<string> fileList = plugin.Sections[dirName].GetLines();
            List<string> encoded = plugin.Sections[$"EncodedFile-{dirName}-{fileName}"].GetLinesOnce();

            return Decode(encoded, out mem);
        }
  
        /// <summary>
        /// Return true if fail
        /// </summary>
        /// <param name="encodedList"></param>
        /// <param name="mem"></param>
        /// <returns></returns>
        public static bool Decode(List<string> encodedList, out MemoryStream mem)
        {
            mem = null;
            if (Ini.GetKeyValueFromLine(encodedList[0], out string key, out string value))
                return true; // Error

            int.TryParse(value, out int blockCount);
            encodedList.RemoveAt(0);

            // Each line is 64KB block
            if (Ini.GetKeyValueFromLines(encodedList, out List<string> keys, out List<string> base64Blocks))
                return true; // Error
            keys = null; // Please GC this

            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < base64Blocks.Count; i++)
                builder.Append(base64Blocks[i]);
                
            switch (builder.Length % 4)
            {
                case 0:
                case 1:
                    break;
                case 2:
                    builder.Append("==");
                    break;
                case 3:
                    builder.Append("=");
                    break;
            }
                
            string encoded = builder.ToString();
            builder = null; // Please GC this
            byte[] decoded = Convert.FromBase64String(encoded);
            encoded = null; // Please GC this
            if (decoded[0] == 0x78 && decoded[1] == 0x9c)
            { // Type 1, encoded with Zlib. 
                MemoryStream zlibMem = new MemoryStream(decoded);
                decoded = null;
                // Remove zlib magic number, converting to deflate data stream
                zlibMem.ReadByte(); // 0x78
                zlibMem.ReadByte(); // 0x9c

                // DeflateStream internally use zlib library, starting from .Net 4.5
                mem = new MemoryStream();
                DeflateStream zlibStream = new DeflateStream(zlibMem, CompressionMode.Decompress);
                mem.Position = 0;
                zlibStream.CopyTo(mem);
                zlibStream.Close();
            }
            else
            { // Type 2, for already compressed file
                // Main file : encoded without zlib
                // Metadata at Footer : zlib compressed -> do not use. Maybe for integrity purpose?
                bool failure = true;
                for (int i = decoded.Length - 1; 0 < i; i--)
                {
                    if (decoded[i - 1] == 0x78 && decoded[i] == 0x9c)
                    { // Found footer zlib stream
                        int idx = i - 1;
                        byte[] body = decoded.Take(idx).ToArray();
                        // byte[] footer = decoded.Skip(idx).ToArray();
                        mem = new MemoryStream(body);
                        failure = false;
                    }
                }
                if (failure)
                    return true;
            }

            return false;
        }
        #endregion
    }
}
