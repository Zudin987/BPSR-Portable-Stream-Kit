using System.Text;

namespace BPSRStreamKit.Infrastructure;

public static class AtomicFile
{
    public static void WriteAllText(string path, string content)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);

        var temp = path + ".streamkit-tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(temp, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
        }
    }

    public static void WriteAllLines(string path, IEnumerable<string> lines) =>
        WriteAllText(path, string.Join(Environment.NewLine, lines) + Environment.NewLine);
}
