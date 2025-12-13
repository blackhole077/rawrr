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

    public static ISequenceFileAccess CreateSequenceFile(string filePath)
    {
        ISequenceFileAccess seqFile = SequenceFileReaderFactory.ReadFile(filePath);
        if (!seqFile.IsOpen || seqFile.IsError)
        {
            Console.WriteLine("Unable to access the sequence file using the RawFileReader class!");
            // Check for any errors in the sequence file
            if (seqFile.IsError)
            {
                Console.WriteLine("Error opening ({0}) - {1}", seqFile.FileError, filePath);

                Environment.Exit(1);
            }
        }

        return seqFile;
    }

    public static IInstrumentMethodFileAccess CreateInstrumentMethodFile(string filePath)
    {
        try
        {
            // Normalize path to handle spaces and special characters
            filePath = Path.GetFullPath(filePath);

            Console.WriteLine($"Attempting to open method file: {filePath}");
            Console.WriteLine($"File exists: {File.Exists(filePath)}");

            IInstrumentMethodFileAccess methodFile = InstrumentMethodReaderFactory.ReadFile(filePath);

            if (!methodFile.IsOpen || methodFile.IsError)
            {
                Console.WriteLine("Unable to access the instrument method file using the RawFileReader class!");
                if (methodFile.IsError)
                {
                    Console.WriteLine("Error opening ({0}) - {1}", methodFile.FileError, filePath);
                    Environment.Exit(1);
                }
            }
            return methodFile;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Exception while opening method file: {ex.GetType().Name}");
            Console.WriteLine($"Message: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"Inner exception: {ex.InnerException.Message}");
            }
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
            throw;
        }
    }
}