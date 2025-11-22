
public static class FileIOHelper
{
    public static StreamWriter CreateStreamWriter(string filePath)
    {
        return new System.IO.StreamWriter(filePath, false);
    }

    public static void WriteTextToFile(string filePath, string content)
    {
        using (var file = CreateStreamWriter(filePath))
        {
                file.Write(content);
            }
    }

    public static void WriteInfoLine(StreamWriter file, string key, object value)
    {
            file.WriteLine($"e$info$`{key}` <- '{value}'");
    }

    public static void WriteInfoLines(StreamWriter file, List<(string key, object value)> entries)
    {
        foreach (var entry in entries)
        {
            WriteInfoLine(file, entry.key, entry.value);
        }
    }

    private static void WriteUserText(StreamWriter file, string[] userText)
    {
        for (int i = 0; i < Math.Min(userText.Length, 5); i++)
        {
            WriteInfoLine(file, $"User text {i}", userText[i]);
        }
    }
}