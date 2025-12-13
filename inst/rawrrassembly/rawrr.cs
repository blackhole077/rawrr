/*
aGetTrailerExtraInformationdapded from the ThermoFischer `Hello, world!` example provided by Jim Shofstahl
see URL http://planetorbitrap.com/rawfilereader#.WjkqIUtJmL4
the ThermoFisher library has to be manual downloaded and installed
Please read the License document
Witold Wolski <wew@fgcz.ethz.ch> and Christian Panse <cp@fgcz.ethz.ch> and Christian Trachsel
2017-09-25 Zurich, Switzerland
2018-04-24 Zurich, Switzerland
2018-06-04 San Diego, CA, USA added xic option
2018-06-28 added xic and scan option
2018-07-24 bugfix
2018-11-23 added scanFilter option
2019-01-28 extract monoisotopicmZ attribute; include segments in MGF iff no centroid data are availbale
2019-05-28 save info as Yaml
2020-11-27 fix basePeak issue #21
2020-08-12 added headerR option
2020-08-26 readSpectrum backend
2021-05-03 reorder xic arguments
2025-05-27 https://github.com/fgcz/rawrr/issues/84
*/

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Collections;
using System.Linq;
using System.CommandLine;
using System.CommandLine.Parsing;
using ThermoFisher.CommonCore.Data;
using ThermoFisher.CommonCore.Data.Business;
using ThermoFisher.CommonCore.Data.FilterEnums;
using ThermoFisher.CommonCore.Data.Interfaces;
using ThermoFisher.CommonCore.MassPrecisionEstimator;
using ThermoFisher.CommonCore.RawFileReader;


namespace FGCZExtensions
{
    /// <summary>
    /// The StringExtension  class
    /// </summary>
    public static class StringExtension
    {

        /// <summary>
        /// make all the existing header names distinct
        /// </summary>
        /// <param name="s"> a string</param>
        /// <returns>a string</returns>
        public static string CleanRawfileTrailerHeader(this string s)
        {
            return (s.Replace(" ", "")
            .Replace("#", "")
            .Replace("m/z", "mZ")
            .Replace("M/Z", "mZ")
            .Replace("(", "")
            .Replace(".", "")
            .Replace(")", "")
            .Replace(":", "")
            .Replace("-", "")
            .Replace("=", ""));
        }
    }

    /// <summary>
    /// utilize the new ThermoFisher RawFileReader
    /// </summary>
    public static partial class IRawDataPlusExtension
    {

        /// <summary>
        /// Generates an R script containing header information extracted from a Thermo RAW file.
        /// The function writes metadata to the specified file in R code format, populating the <c>info</c> list.
        ///
        /// The extracted metadata includes:
        /// <list type="bullet">
        ///   <item><description>File information: RAW file name, version, creation date, operator, instrument count, and description.</description></item>
        ///   <item><description>Instrument information: model, name, method file, serial number, software and firmware versions, units, and mass resolution.</description></item>
        ///   <item><description>Scan information: total number of scans, number of MS2 scans, scan range, time range, and mass range.</description></item>
        ///   <item><description>Filter information: scan filters for the first and last scans, and total number of unique filters.</description></item>
        ///   <item><description>Sample information: details from the sample, including user-provided text.</description></item>
        /// </list>
        /// This function is intended to facilitate downstream analysis in R by providing a comprehensive summary of the RAW file's metadata.
        /// </summary>
        /// <param name="rawFile">The RAW file object to extract metadata from.</param>
        /// <param name="filename">The path to the output file where the R code will be written.</param>
        public static void GenerateHeaderInformationAsRCode(this IRawDataPlus rawFile, string filename, Device device = Device.MS, int deviceNumber = 1)
        {
            using (var file = FileIOHelper.CreateStreamWriter(filename))
            {

                // Need to select instrument before getting instrument data
                rawFile.SelectInstrument(device, deviceNumber);
                IFileHeader fileHeader = rawFile.FileHeader;
                IRunHeader runHeader = rawFile.RunHeaderEx;
                ISampleInformation sampleInfo = rawFile.SampleInformation;
                InstrumentData instrumentData = rawFile.GetInstrumentData();
                // File information
                List<(string key, object value)> fileInfoEntries = ExtractFileHeaderData(rawFile);
                // Instrument information
                List<(string key, object value)> instrumentInfoEntries = ExtractInstrumentData(rawFile, device, deviceNumber);
                instrumentInfoEntries.Add(("Mass resolution", $"{runHeader.MassResolution:F3}"));
                // Instrument methods
                List<(string name, object value)> instrumentMethods = ExtractInstrumentMethods(rawFile, device, deviceNumber);
                // Scan information
                List<(string key, object value)> scanInfoEntries = ExtractScanData(rawFile, device, deviceNumber);
                // Sample information and user text
                List<(string key, object value)> sampleInfoEntries = ExtractSampleInfo(sampleInfo);

                // Write out information we've gathered
                List<List<(string key, object value)>> allInfoEntries = new List<List<(string key, object value)>>()
                    {
                        fileInfoEntries,
                        instrumentInfoEntries,
                        instrumentMethods,
                        scanInfoEntries,
                        sampleInfoEntries
                    };
                string rCode = RCodeWriter.WriteRListOfLists("info", allInfoEntries, new List<string> { "File information", "Instrument information", "Instrument methods", "Scan information", "Sample information" });
                file.WriteLine(rCode);
            }
        }

        public static void GetIndex(this IRawDataPlus rawFile, Device device = Device.MS, int deviceNumber = 1)
        {
            rawFile.SelectInstrument(device, deviceNumber);
            int firstScanNumber = rawFile.RunHeaderEx.FirstSpectrum;
            int lastScanNumber = rawFile.RunHeaderEx.LastSpectrum;

            double charge, precursorMass;
            double monoIsotopicMz;
            int masterScan, dependencyType;

            Console.WriteLine("scan;scanType;StartTime;precursorMass;MSOrder;charge;masterScan;dependencyType;monoisotopicMz");

            Dictionary<string, string> ScanTrailerDict;

            foreach (int scanNumber in Enumerable.Range(firstScanNumber, lastScanNumber))
            {
                var scanTrailer = rawFile.GetTrailerExtraInformation(scanNumber);
                var scanStatistics = rawFile.GetScanStatsForScanNumber(scanNumber);
                var scanEvent = rawFile.GetScanEventForScanNumber(scanNumber);
                var scanFilter = rawFile.GetFilterForScanNumber(scanNumber);

                // TODO(cpanse): implement a public class ScanTrailer
                ScanTrailerDict = new Dictionary<string, string>();
                foreach (var (key, value) in Enumerable.Range(0, scanTrailer.Length).Select(i => (scanTrailer.Labels[i], scanTrailer.Values[i])))
                { ScanTrailerDict[key] = value.Trim(); }

                try
                {
                    var reaction0 = scanEvent.GetReaction(0);
                    precursorMass = reaction0.PrecursorMass;
                }
                catch
                {
                    precursorMass = -1;
                }

                if (!double.TryParse(ScanTrailerDict.GetValueOrDefault("Charge State:", "-1"), out charge))
                {
                    charge = -1;
                }

                if (!int.TryParse(ScanTrailerDict.GetValueOrDefault("Master Scan Number:", "-1"), out masterScan))
                {
                    masterScan = -1;
                }

                if (!int.TryParse(ScanTrailerDict.GetValueOrDefault("Dependency Type:", "-1"), out dependencyType))
                {
                    dependencyType = -1;
                }

                if (!double.TryParse(ScanTrailerDict.GetValueOrDefault("Monoisotopic M/Z:", "-1"), out monoIsotopicMz))
                {
                    monoIsotopicMz = -1.0;
                }

                Console.WriteLine("{0};{1};{2};{3};{4};{5};{6};{7};{8}", scanNumber,
                scanStatistics.ScanType.ToString(),
                scanStatistics.StartTime,
                precursorMass,
                scanFilter.MSOrder.ToString(),
                charge,
                masterScan,
                dependencyType,
                monoIsotopicMz);
            }
        }

        public static void WriteTrailerLabel(this IRawDataPlus rawFile, Device device = Device.MS, int deviceNumber = 1)
        {
            rawFile.SelectInstrument(device, deviceNumber);
            foreach (int scanNumber in Enumerable.Range(rawFile.RunHeaderEx.FirstSpectrum, rawFile.RunHeaderEx.LastSpectrum))
            {
                var scanTrailer = rawFile.GetTrailerExtraInformation(scanNumber);
                Console.WriteLine(string.Join("\n", scanTrailer.Labels.ToArray()));
                return;
            }
        }

        public static void WriteTrailerValues(this IRawDataPlus rawFile, string label, Device device = Device.MS, int deviceNumber = 1)
        {
            rawFile.SelectInstrument(device, deviceNumber);
            Dictionary<string, string> ScanTrailerDict;
            foreach (int scanNumber in Enumerable.Range(rawFile.RunHeaderEx.FirstSpectrum, rawFile.RunHeaderEx.LastSpectrum))
            {
                var scanTrailer = rawFile.GetTrailerExtraInformation(scanNumber);

                // TODO(cpanse): implement a public class ScanTrailer
                ScanTrailerDict = new Dictionary<string, string>();
                foreach (var (key, value) in Enumerable.Range(0, scanTrailer.Length).Select(i => (scanTrailer.Labels[i], scanTrailer.Values[i])))
                { ScanTrailerDict[key] = value.Trim(); }

                if (ScanTrailerDict.ContainsKey(label))
                {
                    Console.WriteLine(ScanTrailerDict[label]);
                }
                else
                {
                    Console.WriteLine("NA");
                }
            }
        }

        public static void GetTuneLogs(this IRawDataPlus rawFile, Device device = Device.MS, int deviceNumber = 1)
        {
            if (device != Device.MS)
            {
                Console.WriteLine("# Tune logs are only available for Mass Spectrometer data.");
                return;
            }
            rawFile.SelectInstrument(device, deviceNumber);
            int tuneLogCount = rawFile.GetTuneDataCount();
            for (int i = 0; i < tuneLogCount; i++)
            {
                var tuneLogHeaders = rawFile.GetTuneDataHeaderInformation();
                var tuneLog = rawFile.GetTuneData(i);
                int numEntries = tuneLogHeaders.Length;
                Console.WriteLine("Tune log {0} has {1} entries.", i, numEntries);
                for (int logCount = 0; logCount < numEntries; logCount++)
                {
                    Console.WriteLine("{0}: {1}", tuneLogHeaders[logCount].Label, tuneLog.Values[logCount]);
                }
            }
        }
    } // end IRawDataPlusExtension class
} // end FGCZExtensions namespace

namespace FGCZ_Raw
{
    using FGCZExtensions;
    using System.Reflection;
    using System.Runtime.Loader;

    internal static class Program
    {
        // Add assembly resolver to help load ThermoFisher DLLs
        static Program()
        {
            AssemblyLoadContext.Default.Resolving += OnAssemblyResolving;
        }

        private static Assembly OnAssemblyResolving(AssemblyLoadContext context, AssemblyName assemblyName)
        {
            // Get the directory where rawrr.exe is located
            string exeDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            
            // Try to load the assembly from the same directory
            string assemblyPath = Path.Combine(exeDir, $"{assemblyName.Name}.dll");
            
            Console.WriteLine($"[Assembly Resolver] Looking for: {assemblyName.Name}");
            Console.WriteLine($"[Assembly Resolver] Trying path: {assemblyPath}");
            
            if (File.Exists(assemblyPath))
            {
                Console.WriteLine($"[Assembly Resolver] Found and loading: {assemblyPath}");
                try
                {
                    return context.LoadFromAssemblyPath(assemblyPath);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Assembly Resolver] Failed to load: {ex.Message}");
                }
            }
            
            return null;
        }

        /// <summary>
        /// Reads the base peak chromatogram for the RAW file
        /// </summary>
        /// <param name="rawFile">
        /// The RAW file being read
        /// </param>
        /// <param name="startScan">
        /// Start scan for the chromatogram
        /// </param>
        /// <param name="endScan">
        /// End scan for the chromatogram
        /// </param>
        /// <param name="outputData">
        /// The output data flag.
        /// </param>
        /// <param name="filter">
        /// The chromatic filter flag.
        /// </param>
        private static void GetChromatogram(IRawDataPlus rawFile, string filename, string filter = "ms", Device device = Device.MS, int deviceNumber = 1)
        {
            rawFile.SelectInstrument(device, deviceNumber);
            var startScan = rawFile.RunHeaderEx.FirstSpectrum;
            var endScan = rawFile.RunHeaderEx.LastSpectrum;
            if (IsValidFilter(rawFile, filter) == false)
            {
                Console.WriteLine("# '{0}' is not a valid filter string.", filter);
                return;
            }

            using (var file = FileIOHelper.CreateStreamWriter(filename))
            {
                // TODO(tk@fgcz.ethz.ch): check mass interval for chromatograms and its dep for diff MS detector types
                // TODO(cp@fgcz.ethz.ch): return mass intervals to the R environment
                // Define the settings for getting the Base Peak chromatogram
                ChromatogramTraceSettings settingsTIC = new ChromatogramTraceSettings(TraceType.TIC) { Filter = filter };
                ChromatogramTraceSettings settingsBasePeak = new ChromatogramTraceSettings(TraceType.BasePeak)
                {
                    Filter = filter,
                    MassRanges = new[] { ThermoFisher.CommonCore.Data.Business.Range.Create(100, 1805) }
                };
                ChromatogramTraceSettings settingsMassRange = new ChromatogramTraceSettings(TraceType.MassRange)
                {
                    Filter = filter,
                    MassRanges = new[] { ThermoFisher.CommonCore.Data.Business.Range.Create(50, 2000000) }
                };

                // Get the chromatogram from the RAW file.
                var dataTIC = rawFile.GetChromatogramData(new IChromatogramSettings[] { settingsTIC }, startScan, endScan);
                var dataMassRange = rawFile.GetChromatogramData(new IChromatogramSettings[] { settingsMassRange }, startScan, endScan);
                var dataBasePeak = rawFile.GetChromatogramData(new IChromatogramSettings[] { settingsBasePeak }, startScan, endScan);

                // Split the data into the chromatograms
                var traceTIC = ChromatogramSignal.FromChromatogramData(dataTIC);
                var traceMassRange = ChromatogramSignal.FromChromatogramData(dataMassRange);
                var traceBasePeak = ChromatogramSignal.FromChromatogramData(dataBasePeak);


                if (traceBasePeak[0].Length > 0)
                {

                    // Print the chromatogram data (time, intensity values)
                    file.WriteLine("# TIC chromatogram ({0} points)", traceTIC[0].Length);
                    file.WriteLine("# Base Peak chromatogram ({0} points)", traceBasePeak[0].Length);
                    file.WriteLine("# MassRange chromatogram ({0} points)", traceMassRange[0].Length);

                    file.WriteLine("rt;intensity.BasePeak;intensity.TIC;intensity.MassRange");

                    for (int i = 0; i < traceBasePeak[0].Length; i++)
                    {
                        file.WriteLine("{1:F3};{2:F0};{3:F0};{4:F0}", i, traceBasePeak[0].Times[i], traceBasePeak[0].Intensities[i], traceTIC[0].Intensities[i], traceMassRange[0].Intensities[i]);
                    }

                }
                file.WriteLine();
            }

        }

        private static bool IsValidFilter(IRawDataPlus rawFile, string filter)
        {
            if (rawFile.GetFilterFromString(filter) == null)
            {
                return false;
            }
            return true;
        }

        private static void ExtractIonChromatogramAsRCode(IRawDataPlus rawFile, int startScan, int endScan, List<double> massList,
        double ppmError, string filename, string filter = "ms")
        {

            if (IsValidFilter(rawFile, filter) == false)
            {
                using (var file = FileIOHelper.CreateStreamWriter(filename))
                {
                    file.WriteLine("e$error <- \"'{0}' is not a valid filter string.\";", filter);
                }
                return;
            }

            List<ChromatogramTraceSettings> settingList = new List<ChromatogramTraceSettings>();

            foreach (var mass in massList)
            {
                double massError = (0.5 * ppmError * mass) / 1000000;
                ChromatogramTraceSettings settings = new ChromatogramTraceSettings(TraceType.MassRange)
                {
                    Filter = filter,
                    MassRanges = new[] { ThermoFisher.CommonCore.Data.Business.Range.Create(mass - massError, mass + massError) }
                };

                settingList.Add(settings);
            }

            IChromatogramSettings[] allSettings = settingList.ToArray();

            var data = rawFile.GetChromatogramData(allSettings, startScan, endScan);

            // Split the data into the chromatograms
            var trace = ChromatogramSignal.FromChromatogramData(data);

            using (var file = FileIOHelper.CreateStreamWriter(filename))
            {
                file.WriteLine("#R\n");

                for (int i = 0; i < trace.Length; i++)
                {
                    List<double> tTime = new List<double>();
                    List<double> tIntensities = new List<double>();

                    for (int j = 0; j < trace[i].Times.Count; j++)
                    {
                        //   if (trace[i].Intensities[j] > 0)
                        {
                            tTime.Add(trace[i].Times[j]);
                            tIntensities.Add(trace[i].Intensities[j]);
                        }

                    }

                    file.WriteLine("e$chromatogram[[{0}]] <- list(", i + 1);
                    file.WriteLine("\tfilter = '{0}',", filter);
                    file.WriteLine("\tppm = {0},", ppmError);
                    file.WriteLine("\tmass = {0},", massList[i]);
                    file.WriteLine("\ttimes = c(" + string.Join(",", tTime) + "),");
                    file.WriteLine("\tintensities = c(" + string.Join(",", tIntensities) + ")");
                    file.WriteLine(");");
                }
            }
        }

        public static void ExtractLCPressureFromAnalogToDigitalCardAsRCode(IRawDataPlus rawFile, string outputFileName)
        {
            int numberOfAnalogToDigitalDevices = rawFile.GetInstrumentCountOfType(Device.Analog);
            List<List<(string key, object value)>> allLiquidChromatographyPressures = new List<List<(string key, object value)>>();
            for (int instrumentIndex = 1; instrumentIndex < numberOfAnalogToDigitalDevices + 1; instrumentIndex++)
            {
                rawFile.SelectInstrument(Device.Analog, instrumentIndex); // Get Analog-to-Digital card data
                var runHeader = rawFile.RunHeaderEx;
                int startScan = runHeader.FirstSpectrum;
                int endScan = runHeader.LastSpectrum;
                InstrumentData analogToDigitalCardInfo = rawFile.GetInstrumentData();
                string[] analogToDigitalCardChannelLabels = analogToDigitalCardInfo.ChannelLabels;
                List<(string key, object value)> liquidChromatographyPressures = new List<(string key, object value)>();
                if (analogToDigitalCardInfo.ChannelLabels != null && analogToDigitalCardInfo.ChannelLabels.Length > 0)
                {
                    for (int i = 0; i < analogToDigitalCardInfo.ChannelLabels.Length; i++)
                    {
                        string label = analogToDigitalCardInfo.ChannelLabels[i];
                        Console.WriteLine($"Channel {i + 1}: {label}");

                        // Check if this channel is pressure-related
                        if (label.ToLower().Contains("pressure"))
                        {
                            TraceType traceType = TraceType.StartPCA2DChromatogramTraces + (i + 1);
                            ChromatogramTraceSettings settings = new ChromatogramTraceSettings(traceType);
                            IChromatogramData data = rawFile.GetChromatogramData(
                                new IChromatogramSettings[] { settings },
                                rawFile.RunHeaderEx.FirstSpectrum,
                                rawFile.RunHeaderEx.LastSpectrum
                            );
                            // Convert to signal for easier access
                            ChromatogramSignal[] traces = ChromatogramSignal.FromChromatogramData(data);
                            // Access the pressure data
                            if (traces != null && traces.Length > 0)
                            {
                                foreach (ChromatogramSignal trace in traces)
                                {
                                    for (int j = 0; j < trace.Length; j++)
                                    {
                                        liquidChromatographyPressures.Add(($"Time", trace.Times[j]));
                                        liquidChromatographyPressures.Add(($"Pressure", trace.Intensities[j]));
                                    }
                                    allLiquidChromatographyPressures.Add(liquidChromatographyPressures);
                                }
                            }
                        }
                    }
                }
                // Write out information we've gathered
                if (string.IsNullOrEmpty(outputFileName))
                {
                    // write to console
                    for (int i = 0; i < allLiquidChromatographyPressures.Count; i++)
                    {
                        Console.WriteLine(string.Join("\n", allLiquidChromatographyPressures[i].Select(entry => $"{entry.key}: {entry.value}")));
                    }
                }
                else
                {
                    using (var file = FileIOHelper.CreateStreamWriter(outputFileName))
                    {
                        string rCode = RCodeWriter.WriteRListOfLists("LCPressures", allLiquidChromatographyPressures);
                        file.WriteLine(rCode);
                    }
                }
            }
        }

        private static void ExtractSampleAndMethodFromSequenceFile(ISequenceFileAccess seqFile, string outputFileName)
        {
            int sampleCount = seqFile.Samples.Count;
            using (var file = FileIOHelper.CreateStreamWriter(outputFileName))
            {
                for (int i = 0; i < sampleCount; i++)
                {
                    var sample = seqFile.Samples[i];
                    file.WriteLine("# Sample {0}: SampleId={1}, SampleName={2}, SampleType={3}, Comment={4}, Vial={5}, InjectionVolume={6}, Barcode={7}, BarcodeStatus={8}, CalibrationLevel={9}, DilutionFactor={10}, InstrumentMethodFile={11}, RawFileName={12}, CalibrationFile={13}, IstdAmount={14}, RowNumber={15}, Path={16}, ProcessingMethodFile={17}, SampleVolume={18}, SampleWeight={19}, UserText={20}",
                        i + 1,
                        sample.SampleId,
                        sample.SampleName,
                        sample.SampleType,
                        sample.Comment,
                        sample.Vial,
                        sample.InjectionVolume,
                        sample.Barcode,
                        sample.BarcodeStatus,
                        sample.CalibrationLevel,
                        sample.DilutionFactor,
                        sample.InstrumentMethodFile,
                        sample.RawFileName,
                        sample.CalibrationFile,
                        sample.IstdAmount,
                        sample.RowNumber,
                        sample.Path,
                        sample.ProcessingMethodFile,
                        sample.SampleVolume,
                        sample.SampleWeight,
                        string.Join(",", sample.UserText));
                }
            }
        }

        private static void ExtractInformationFromMethodFile(IInstrumentMethodFileAccess methodFile, string outputFileName)
        {
            var devices = methodFile.Devices;
            using (var file = FileIOHelper.CreateStreamWriter(outputFileName))
            {
                file.WriteLine("# Devices and Method information from the method file:");
                foreach (var deviceEntry in devices)
                {
                    file.WriteLine($"Device Key: {deviceEntry.Key}");
                    file.WriteLine($"MethodText: {deviceEntry.Value.MethodText}");
                }
            }
        }

        private static void Main(string[] args)
        {
            Argument<string> inputRawFileArg = new Argument<string>("inputfile")
            {
                Description = "Input File. Thermo RAW files, sequence files (.sld) or instrument method files (.meth) are accepted."
            };
            Argument<string> outputFileArg = new Argument<string>("outputfile")
            {
                Description = "Output file",
                DefaultValueFactory = _ => null
            };
            RootCommand rootCommand = new RootCommand("rawrr - A .NET library and command line tool to access Thermo Fisher Scientific RAW files.");
            // `headerR` command and arguments
            Command headerRCommand = new Command("headerR", "Writes the raw file's meta data as R code to a file.");
            headerRCommand.Arguments.Add(inputRawFileArg);
            headerRCommand.Arguments.Add(outputFileArg);
            headerRCommand.SetAction((ParseResult parseResult) =>
            {
                string inputFile = parseResult.GetValue(inputRawFileArg);
                Console.WriteLine($"Input file: {inputFile}");
                string outputFile = parseResult.GetValue(outputFileArg);
                Console.WriteLine($"Output file: {outputFile}");
                using (var rawFile = FileIOHelper.CreateRawDataPlus(inputFile))
                {
                    rawFile.GenerateHeaderInformationAsRCode(outputFile);
                }
            });
            // `trailer` command and arguments
            Option<string> trailerOutputFile = new Option<string>("--output")
            {
                Description = "The name of the output file. If not provided, prints trailer labels to console.",
                DefaultValueFactory = _ => null
            };
            Command trailerCommand = new Command("trailer", "Prints all trailer labels. If an output file is provided, prints trailer values of the specified label to the file.");
            trailerCommand.Arguments.Add(inputRawFileArg);
            trailerCommand.Options.Add(trailerOutputFile);
            trailerCommand.SetAction((ParseResult parseResult) =>
            {
                string inputFile = parseResult.GetValue(inputRawFileArg);
                string outputFile = parseResult.GetValue(trailerOutputFile);
                using (var rawFile = FileIOHelper.CreateRawDataPlus(inputFile))
                {
                    if (string.IsNullOrEmpty(outputFile))
                    {
                        rawFile.WriteTrailerLabel();
                    }
                    else
                    {
                        rawFile.WriteTrailerValues(outputFile);
                    }
                }
            });
            // `filter` command and arguments
            Argument<string> filterStringArg = new Argument<string>("filterstring")
            {
                Description = "Filter string"
            };
            Argument<int> filterPrecisionArg = new Argument<int>("precision")
            {
                Description = "Filter precision"
            };
            Command filterCommand = new Command("filter", "List all scan ids that pass the filter string (option 2).");
            filterCommand.Arguments.Add(inputRawFileArg);
            filterCommand.Arguments.Add(filterStringArg);
            filterCommand.Arguments.Add(filterPrecisionArg);
            filterCommand.Arguments.Add(outputFileArg);
            filterCommand.SetAction((ParseResult parseResult) =>
            {
                string filename = parseResult.GetValue(inputRawFileArg);
                string filterString = parseResult.GetValue(filterStringArg);
                int precision = parseResult.GetValue(filterPrecisionArg);
                string outputFilename = parseResult.GetValue(outputFileArg);

                using (var rawFile = FileIOHelper.CreateRawDataPlus(filename))
                {
                    if (!IsValidFilter(rawFile, filterString))
                    {
                        Console.Write("\nThe provided filter string '{0}' is not valid. Please check the filter syntax.\n", filterString);
                        Environment.Exit(1);
                    }
                    using (var file = FileIOHelper.CreateStreamWriter(outputFilename))
                    {
                        foreach (var ss in rawFile.GetFilteredScanEnumerator(rawFile.GetFilterFromString(filterString, precision)).ToArray())
                        {
                            file.WriteLine(ss);
                        }
                    }
                }
            });
            // `isValidFilter` command and arguments
            Argument<string> isValidFilterStringArg = new Argument<string>("filterstring")
            {
                Description = "Filter string"
            };
            Command isValidFilterCommand = new Command("isValidFilter", "Checks whether the provided argument string (option 2) is a valid filter.");
            isValidFilterCommand.Arguments.Add(inputRawFileArg);
            isValidFilterCommand.Arguments.Add(isValidFilterStringArg);
            isValidFilterCommand.SetAction((ParseResult parseResult) =>
            {
                string filename = parseResult.GetValue(inputRawFileArg);
                string filterstring = parseResult.GetValue(isValidFilterStringArg);
                using (var rawFile = FileIOHelper.CreateRawDataPlus(filename))
                {
                    rawFile.SelectInstrument(Device.MS, 1);
                    Console.WriteLine(IsValidFilter(rawFile, filterstring).ToString());
                }
            });
            // `getFilters` command and arguments
            Command getFiltersCommand = new Command("getFilters", "List all scan filters of a given raw file.");
            getFiltersCommand.Arguments.Add(inputRawFileArg);
            getFiltersCommand.SetAction((ParseResult parseResult) =>
            {
                string inputFile = parseResult.GetValue(inputRawFileArg);
                using (var rawFile = FileIOHelper.CreateRawDataPlus(inputFile))
                {
                    rawFile.SelectInstrument(Device.MS, 1);
                    foreach (var filter in rawFile.GetFilters())
                    {
                        Console.WriteLine(filter.ToString());
                    }
                }
            });
            // `chromatogram` command and arguments
            Argument<string> chromatogramFilterStringArg = new Argument<string>("filter")
            {
                Description = "Filter string. Defaults to 'ms'.",
                DefaultValueFactory = _ => "ms",
            };
            var chromatogramCommand = new Command("chromatogram", "Extracts base peak and total ion count chromatograms into a file.");
            chromatogramCommand.Arguments.Add(inputRawFileArg);
            chromatogramCommand.Arguments.Add(outputFileArg);
            chromatogramCommand.Arguments.Add(chromatogramFilterStringArg);
            chromatogramCommand.SetAction((ParseResult parseResult) =>
            {
                string inputfile = parseResult.GetValue(inputRawFileArg);
                // We set a default name here if none is provided to avoid setting it in the argument itself
                string outputfile = parseResult.GetValue(outputFileArg) ?? "chromatogram.csv";
                string filterstring = parseResult.GetValue(chromatogramFilterStringArg);
                using (var rawFile = FileIOHelper.CreateRawDataPlus(inputfile))
                {
                    GetChromatogram(rawFile, outputfile, filterstring);
                }
            });
            // `index` command and arguments
            Command indexCommand = new Command("index", "Prints index as csv of all scans.");
            indexCommand.Arguments.Add(inputRawFileArg);
            indexCommand.SetAction((ParseResult parseResult) =>
            {
                string inputfile = parseResult.GetValue(inputRawFileArg);
                using (var rawFile = FileIOHelper.CreateRawDataPlus(inputfile))
                {
                    rawFile.GetIndex();
                }
            });
            // `scans` command and arguments
            Argument<string> scansScanFileArg = new Argument<string>("scanfile")
            {
                Description = "File containing scan ids to extract."
            };
            Command scansCommand = new Command("scans", "Extracts scans (spectra) of a given ID as an R file.");
            scansCommand.Arguments.Add(inputRawFileArg);
            scansCommand.Arguments.Add(scansScanFileArg);
            scansCommand.Arguments.Add(outputFileArg);
            scansCommand.SetAction((ParseResult parseResult) =>
            {
                string inputfile = parseResult.GetValue(inputRawFileArg);
                string outputfile = parseResult.GetValue(outputFileArg);
                string scanIdsFile = parseResult.GetValue(scansScanFileArg);
                List<int> scanIds = new List<int>();
                using (var rawFile = FileIOHelper.CreateRawDataPlus(inputfile))
                {
                    rawFile.SelectInstrument(Device.MS, 1);
                    // Read scan ids from file

                    foreach (var line in File.ReadAllLines(scanIdsFile))
                    {
                        if (int.TryParse(line.Trim(), out int scanNumber))
                        {
                            if (scanNumber > 0)
                            {
                                scanIds.Add(scanNumber);
                            }
                        }
                    }
                    if (scanIds.Count == 0)
                    {
                        rawFile.WriteSpectrumAsRCode0(outputfile);
                    }
                    else
                    {
                        rawFile.WriteSpectrumAsRCode(outputfile, scanIds);
                    }
                }
            });
            Argument<string> bareboneScanFileArg = new Argument<string>("scanfile")
            {
                Description = "File containing scan ids to extract."
            };
            Command bareboneCommand = new Command("barebone", "Extracts only the spectra of a given ID as an R file.");
            bareboneCommand.Arguments.Add(inputRawFileArg);
            bareboneCommand.Arguments.Add(bareboneScanFileArg);
            bareboneCommand.Arguments.Add(outputFileArg);
            bareboneCommand.SetAction((ParseResult parseResult) =>
            {
                string inputfile = parseResult.GetValue(inputRawFileArg);
                string outputfile = parseResult.GetValue(outputFileArg);
                string scanIdsFile = parseResult.GetValue(bareboneScanFileArg);
                List<int> scanIds = new List<int>();
                using (var rawFile = FileIOHelper.CreateRawDataPlus(inputfile))
                {
                    rawFile.SelectInstrument(Device.MS, 1);
                    // Read scan ids from file
                    foreach (var line in File.ReadAllLines(scanIdsFile))
                    {
                        if (int.TryParse(line.Trim(), out int scanNumber))
                        {
                            if (scanNumber > 0)
                            {
                                scanIds.Add(scanNumber);
                            }
                        }
                    }
                    if (scanIds.Count == 0)
                    {
                        rawFile.WriteSpectrumAsRCode0(outputfile);
                    }
                    else
                    {
                        rawFile.WriteCentroidSpectrumAsRCode(outputfile, scanIds);
                    }
                }
            });
            // `xic` command and arguments
            // Stands for extracted ion chromatogram
            Argument<string> massListFileArg = new Argument<string>("mass-list-file")
            {
                Description = "File containing masses to extract."
            };
            Argument<double> xicPpmArg = new Argument<double>("ppm")
            {
                Description = "Mass tolerance in ppm.",
            };
            Argument<string> xicFilterStringArg = new Argument<string>("--filter")
            {
                Description = "Filter string. Defaults to 'ms'.",
                DefaultValueFactory = _ => "ms",
            };
            var xicCommand = new Command("xic", "Extracts filtered (option 2) ion chromatograms within a given mass and mass tolerance [in ppm] (option 3) xic of a given raw file as R code into a file.");
            xicCommand.Arguments.Add(inputRawFileArg);
            xicCommand.Arguments.Add(xicPpmArg);
            xicCommand.Arguments.Add(xicFilterStringArg);
            xicCommand.Arguments.Add(massListFileArg);
            xicCommand.Arguments.Add(outputFileArg);
            xicCommand.SetAction((ParseResult parseResult) =>
            {
                string inputfile = parseResult.GetValue(inputRawFileArg);
                double ppmError = parseResult.GetValue(xicPpmArg);
                string massListFile = parseResult.GetValue(massListFileArg);
                string outputfile = parseResult.GetValue(outputFileArg);
                List<double> massList = new List<double>();
                using (var rawFile = FileIOHelper.CreateRawDataPlus(inputfile))
                {
                    rawFile.SelectInstrument(Device.MS, 1);
                    // Read masses from file    
                    if (File.Exists(massListFile) == false)
                    {
                        Console.WriteLine($"Mass list file '{massListFile}' does not exist!");
                        Environment.Exit(1);
                    }
                    foreach (var line in File.ReadAllLines(massListFile))
                    {
                        if (double.TryParse(line.Trim(), out double mass))
                        {
                            massList.Add(mass);
                        }
                    }
                    ExtractIonChromatogramAsRCode(rawFile, -1, -1, massList, ppmError, outputfile);
                }
            });
            // `getTuneLogs` command and arguments
            var getTuneLogsCommand = new Command("get-tune-logs", "Retrieves the tune information from the raw file.");
            getTuneLogsCommand.Arguments.Add(inputRawFileArg);
            getTuneLogsCommand.SetAction((ParseResult parseResult) =>
            {
                string inputFile = parseResult.GetValue(inputRawFileArg);
                using (var rawFile = FileIOHelper.CreateRawDataPlus(inputFile))
                {
                    rawFile.GetTuneLogs();
                }
            });
            // `extract-sample-method` command
            Command extractSampleMethodCommand = new Command("extract-sample-method", "Extracts sample and method information from a sequence file.");
            extractSampleMethodCommand.Arguments.Add(inputRawFileArg);
            extractSampleMethodCommand.Arguments.Add(outputFileArg);
            extractSampleMethodCommand.SetAction((ParseResult parseResult) =>
            {
                string inputfile = parseResult.GetValue(inputRawFileArg);
                var seqFile = FileIOHelper.CreateSequenceFile(inputfile);
                string outputFile = parseResult.GetValue(outputFileArg) ?? "sample_method.txt";
                ExtractSampleAndMethodFromSequenceFile(seqFile, outputFile);
            });
            // `get-lc-pressure` command and arguments
            Command getLCPressureCommand = new Command("get-lc-pressure", "Fetches LC Pressure values and exports them as R List.");
            getLCPressureCommand.Arguments.Add(inputRawFileArg);
            getLCPressureCommand.Arguments.Add(outputFileArg);
            getLCPressureCommand.SetAction((ParseResult parseResult) =>
            {
                string inputfile = parseResult.GetValue(inputRawFileArg);
                string outputFile = parseResult.GetValue(outputFileArg);
                using (var rawFile = FileIOHelper.CreateRawDataPlus(inputfile))
                {
                    ExtractLCPressureFromAnalogToDigitalCardAsRCode(rawFile, outputFile);
                }
            });
            // `extract-method-info` command and arguments
            Command extractMethodInfoCommand = new Command("extract-method-info", "Extract method information")
            {
                inputRawFileArg,
                outputFileArg
            };

            extractMethodInfoCommand.SetAction((ParseResult parseResult) =>
            {
                string inputFile = parseResult.GetValue(inputRawFileArg);
                string outputFile = parseResult.GetValue(outputFileArg);
                try
                {
                    Console.WriteLine($"[Debug] Starting extract-method-info");
                    Console.WriteLine($"[Debug] Current Directory: {Directory.GetCurrentDirectory()}");
                    Console.WriteLine($"[Debug] Executable Location: {Assembly.GetExecutingAssembly().Location}");
                    Console.WriteLine($"[Debug] Method File: {inputFile}");
                    Console.WriteLine($"[Debug] Output File: {outputFile}");
                    Console.WriteLine($"[Debug] Method File Exists: {File.Exists(inputFile)}");

                    // List loaded assemblies
                    Console.WriteLine($"[Debug] Loaded Assemblies:");
                    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        Console.WriteLine($"  - {asm.GetName().Name}");
                    }
                    
                    IInstrumentMethodFileAccess methodFile = FileIOHelper.CreateInstrumentMethodFile(inputFile);
                    ExtractInformationFromMethodFile(methodFile, outputFile);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Error] Exception Type: {ex.GetType().FullName}");
                    Console.WriteLine($"[Error] Message: {ex.Message}");
                    Console.WriteLine($"[Error] Stack Trace:\n{ex.StackTrace}");
                    
                    if (ex.InnerException != null)
                    {
                        Console.WriteLine($"[Error] Inner Exception: {ex.InnerException.GetType().FullName}");
                        Console.WriteLine($"[Error] Inner Message: {ex.InnerException.Message}");
                        Console.WriteLine($"[Error] Inner Stack Trace:\n{ex.InnerException.StackTrace}");
                    }
                    
                    Environment.Exit(1);
                }
            });
            // `version` option
            VersionOption versionOption = new VersionOption("--version")
            {
                Description = "Prints the rawrr version."
            };
            // Add the commands as sub-commands to the root command
            rootCommand.Subcommands.Add(headerRCommand);
            rootCommand.Subcommands.Add(trailerCommand);
            rootCommand.Subcommands.Add(filterCommand);
            rootCommand.Subcommands.Add(isValidFilterCommand);
            rootCommand.Subcommands.Add(getFiltersCommand);
            rootCommand.Subcommands.Add(chromatogramCommand);
            rootCommand.Subcommands.Add(indexCommand);
            rootCommand.Subcommands.Add(scansCommand);
            rootCommand.Subcommands.Add(bareboneCommand);
            rootCommand.Subcommands.Add(xicCommand);
            rootCommand.Subcommands.Add(getTuneLogsCommand);
            rootCommand.Subcommands.Add(extractSampleMethodCommand);
            rootCommand.Subcommands.Add(getLCPressureCommand);
            rootCommand.Subcommands.Add(extractMethodInfoCommand);
            rootCommand.Options.Add(versionOption);
            // Parse whatever command has come in and execute it.
            ParseResult parseResult = rootCommand.Parse(args);
            parseResult.Invoke();
        }
    }
}
