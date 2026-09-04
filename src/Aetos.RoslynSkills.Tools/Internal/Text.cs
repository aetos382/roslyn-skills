using System;
using System.IO;
using System.Text;

namespace Aetos.RoslynSkills.Tools.Internal;

internal static class Text
{
    public static (string Content, bool HasBom, string NewLine) ReadPreserving(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        var content = new UTF8Encoding(false).GetString(bom ? bytes.AsSpan(3) : bytes);
        return (content, bom, content.Contains("\r\n") ? "\r\n" : "\n");
    }
}
