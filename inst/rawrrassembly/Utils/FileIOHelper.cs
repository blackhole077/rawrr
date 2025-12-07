using System;
using System.IO;
using System.Collections.Generic;
using ThermoFisher.CommonCore.Data.Interfaces;
using ThermoFisher.CommonCore.RawFileReader;
using ThermoFisher.CommonCore.Data.Business;

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

    public static void WriteUserText(StreamWriter file, string[] userText)
    {
        for (int i = 0; i < Math.Min(userText.Length, 5); i++)
        {
            WriteInfoLine(file, $"User text {i}", userText[i]);
        }
    }
    // Need to figure out how to fix CS0246 error for IRawDataPlus
    public static IRawDataPlus CreateRawDataPlus(string filePath)
    {
        IRawDataPlus rawFile = RawFileReaderAdapter.FileFactory(filePath);
        if (!rawFile.IsOpen || rawFile.IsError)
        {
            Console.WriteLine("Unable to access the RAW file using the RawFileReader class!");
            // Check for any errors in the RAW file
            if (rawFile.IsError)
            {
                Console.WriteLine("Error opening ({0}) - {1}", rawFile.FileError, filePath);

                Environment.Exit(1);
            }

            // Check if the RAW file is being acquired
            if (rawFile.InAcquisition)
            {
                Console.WriteLine("RAW file still being acquired - " + filePath);

                Environment.Exit(1);
            }
        }

        return rawFile;
    }
}