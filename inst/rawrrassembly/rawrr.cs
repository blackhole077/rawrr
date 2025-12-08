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
    public static class IRawDataPlusExtension
    {

        /// <summary>
        /// Generates an R script containing header information extracted from a Thermo RAW file.
        /// The function writes metadata to the specified file in R code format, populating the <c>e$info</c> list.
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
        public static void GenerateHeaderInformationAsRCode(this IRawDataPlus rawFile, string filename)
        {
            using (var file = FileIOHelper.CreateStreamWriter(filename))
            {
                List<(string key, object value)> fileInfoEntries = new List<(string key, object value)>();
                List<(string key, object value)> instrumentInfoEntries = new List<(string key, object value)>();
                List<(string key, object value)> scanInfoEntries = new List<(string key, object value)>();
                file.WriteLine("#R\n");
                file.WriteLine("e$info <- list()\n");

                // Need to select instrument before getting instrument data
                rawFile.SelectInstrument(Device.MS, 1); // Get Mass Spectrometer data
                var fileHeader = rawFile.FileHeader;
                var instrumentData = rawFile.GetInstrumentData();
                var runHeader = rawFile.RunHeaderEx;
                var sampleInfo = rawFile.SampleInformation;

                // File information

                fileInfoEntries.Add(("RAW file", Path.GetFileName(rawFile.FileName)));
                fileInfoEntries.Add(("RAW file version", fileHeader.Revision));
                fileInfoEntries.Add(("Creation date", fileHeader.CreationDate));
                fileInfoEntries.Add(("Operator", fileHeader.WhoCreatedId));
                fileInfoEntries.Add(("Number of instruments", rawFile.InstrumentCount));
                fileInfoEntries.Add(("Description", fileHeader.FileDescription));

                // Instrument information
                instrumentInfoEntries.Add(("Instrument model", instrumentData.Model));
                instrumentInfoEntries.Add(("Instrument name", instrumentData.Name));
                instrumentInfoEntries.Add(("Serial number", instrumentData.SerialNumber));
                instrumentInfoEntries.Add(("Software version", instrumentData.SoftwareVersion));
                instrumentInfoEntries.Add(("Firmware version", instrumentData.HardwareVersion));
                instrumentInfoEntries.Add(("Units", instrumentData.Units));
                instrumentInfoEntries.Add(("Mass resolution", $"{runHeader.MassResolution:F3}"));
                // Instrument methods
                List<(string name, object value)> instrumentMethods = WriteInstrumentMethods(rawFile);

                // Scan information
                int firstScanNumber = runHeader.FirstSpectrum;
                int lastScanNumber = runHeader.LastSpectrum;
                int ms2Count = Enumerable.Range(firstScanNumber, lastScanNumber - firstScanNumber + 1)
                .Count(x => rawFile.GetFilterForScanNumber(x).ToString().Contains("Full ms2"));

                scanInfoEntries.Add(("Number of scans", runHeader.SpectraCount));
                scanInfoEntries.Add(("Number of ms2 scans", ms2Count));
                scanInfoEntries.Add(("Scan range", $"c({firstScanNumber}, {lastScanNumber})"));
                scanInfoEntries.Add(("Time range", $"c({runHeader.StartTime:F2}, {runHeader.EndTime:F2})"));
                scanInfoEntries.Add(("Mass range", $"c({runHeader.LowMass:F4}, {runHeader.HighMass:F4})"));

                // Filter information
                var firstFilter = rawFile.GetFilterForScanNumber(firstScanNumber);
                var lastFilter = rawFile.GetFilterForScanNumber(lastScanNumber);
                scanInfoEntries.Add(("Scan filter (first scan)", firstFilter.ToString()));
                scanInfoEntries.Add(("Scan filter (last scan)", lastFilter.ToString()));
                scanInfoEntries.Add(("Total number of filters", rawFile.GetFilters().Count));

                // Write out information we've gathered
                List<List<(string key, object value)>> allInfoEntries = new List<List<(string key, object value)>>()
                    {
                        fileInfoEntries,
                        instrumentInfoEntries,
                        instrumentMethods,
                        scanInfoEntries,
                        GenerateSampleInfo(sampleInfo),
                    };
                string rCode = RCodeWriter.WriteRListOfLists("e$info", allInfoEntries, new List<string> { "File information", "Instrument information", "Instrument methods", "Scan information", "Sample information" });
                // User text
                for (int i = 0; i < sampleInfo.UserText.Length; i++)
                {
                    string key = $"e$info$`User text {i + 1}`";
                    string value = sampleInfo.UserText[i];
                    rCode += RCodeWriter.WriteRVariable(key, value);
                }
                file.WriteLine(rCode);
            }
        }

        private static List<(string key, object value)> GenerateSampleInfo(ISampleInformation sampleInfo)
        {
            var entries = new List<(string key, object value)>();
            entries.Add(("Sample name", sampleInfo.SampleName));
            entries.Add(("Sample id", sampleInfo.SampleId));
            entries.Add(("Sample type", sampleInfo.SampleType));
            entries.Add(("Sample comment", sampleInfo.Comment));
            entries.Add(("Sample vial", sampleInfo.Vial));
            entries.Add(("Sample volume", sampleInfo.SampleVolume));
            entries.Add(("Sample injection volume", sampleInfo.InjectionVolume));
            entries.Add(("Sample row number", sampleInfo.RowNumber));
            entries.Add(("Sample dilution factor", sampleInfo.DilutionFactor));
            entries.Add(("Sample barcode", sampleInfo.Barcode));
            return entries;
        }

        /// <summary>
        /// Extracts and writes instrument method information to the output R code file.
        ///
        /// This method retrieves all instrument methods from the RAW file and writes them as an R list structure.
        /// Each instrument method includes the instrument's friendly name (or a fallback index-based name) and
        /// the complete method content. The output is formatted as nested R lists for easy parsing.
        ///
        /// The method handles multiple instruments within a single RAW file (e.g., LC pump, MS detector,
        /// autosampler) and writes them in the format:
        /// <code>
        /// e$info$`Instrument methods`[[1]] &lt;- list(
        ///     name = 'LC Pump',
        ///     method = '...'
        /// )
        /// </code>
        ///
        /// Special characters in method content (backslashes, newlines, quotes) are properly escaped
        /// for R string compatibility.
        /// </summary>
        /// <param name="file">The StreamWriter for the output R code file.</param>
        /// <param name="rawFile">The RAW file object containing instrument method information.</param>
        private static List<(string name, object value)> WriteInstrumentMethods(IRawDataPlus rawFile)
        {
            var instrumentMethodsList = new List<(string name, object value)>();
            try
            {
                rawFile.SelectInstrument(Device.MS, 1); // Get Mass Spectrometer data
                // Get all instrument friendly names from the instrument method
                var instrumentFriendlyNames = rawFile.GetAllInstrumentFriendlyNamesFromInstrumentMethod();

                // Get the number of instrument methods
                int methodCount = rawFile.InstrumentMethodsCount;

                instrumentMethodsList.Add(("Number of instrument methods", methodCount));

                for (int i = 0; i < methodCount; i++)
                {
                    // Use friendly name if available, otherwise fall back to index-based name
                    string instrumentName = i < instrumentFriendlyNames.Count()
                        ? instrumentFriendlyNames[i]
                        : $"Instrument_{i}";

                    string methodContent = rawFile.GetInstrumentMethod(i)
                        .Replace("\\", "/")
                        .Replace("\n", "\\n")
                        .Replace("\r", "")
                        .Replace("'", "\\'");

                    instrumentMethodsList.Add((instrumentName, methodContent));
                }
            }
            catch (Exception ex)
            {
                // If method extraction fails, log it but don't crash
                instrumentMethodsList.Add(("Warning: Could not extract instrument methods", ex.Message));
            }
            return instrumentMethodsList;
        }

        public static void GetIndex(this IRawDataPlus rawFile)
        {
            rawFile.SelectInstrument(Device.MS, 1); // Get Mass Spectrometer data
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

        public static void WriteSpectrumAsRcode0(this IRawDataPlus rawFile, string filename)
        {
            rawFile.SelectInstrument(Device.MS, 1); // Get Mass Spectrometer data
            int firstScanNumber = rawFile.RunHeaderEx.FirstSpectrum;
            int lastScanNumber = rawFile.RunHeaderEx.LastSpectrum;
            int charge = -1;
            double precursorMass = -1;
            Dictionary<string, string> ScanTrailerDict;
            List<string> spectrumFileContents = new List<string>();
            foreach (int scanNumber in Enumerable.Range(firstScanNumber, lastScanNumber))
            {
                var scanTrailer = rawFile.GetTrailerExtraInformation(scanNumber);
                var scanStatistics = rawFile.GetScanStatsForScanNumber(scanNumber);
                var scanEvent = rawFile.GetScanEventForScanNumber(scanNumber);
                var scanFilter = rawFile.GetFilterForScanNumber(scanNumber);

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

                if (!int.TryParse(ScanTrailerDict.GetValueOrDefault("Charge State:", "-1"), out charge))
                {
                    charge = -1;
                }

                spectrumFileContents.Add($"e$Spectrum[[{scanNumber}]] <- list(");
                spectrumFileContents.Add($"\tscan = {scanNumber},");
                spectrumFileContents.Add($"\tscanType = \"{scanStatistics.ScanType}\",");
                spectrumFileContents.Add($"\tStartTime = {scanStatistics.StartTime},");
                spectrumFileContents.Add($"\trtinseconds = {Math.Round(scanStatistics.StartTime * 60 * 1000) / 1000},");
                spectrumFileContents.Add($"\tprecursorMass = {precursorMass},");
                spectrumFileContents.Add($"\tMSOrder = '{scanFilter.MSOrder.ToString()}',");
                spectrumFileContents.Add($"\tcharge = {charge}");
                spectrumFileContents.Add(")");

                FileIOHelper.WriteTextToFile(filename, string.Join("\n", spectrumFileContents));
            }
        }



        /// <summary>
        ///    implements
        ///    https://github.com/fgcz/rawrr/issues/43
        /// </summary>
        /// <param name="rawFile"></param>
        /// <param name="filename"></param>
        /// <param name="L"></param>
        public static void WriteCentroidSpectrumAsRcode(this IRawDataPlus rawFile, string filename, List<int> L)
        {
            rawFile.SelectInstrument(Device.MS, 1); // Get Mass Spectrometer data
            int count = 1;
            int charge = -1;
            Dictionary<string, string> ScanTrailerDict;
            var trailerFields = rawFile.GetTrailerExtraHeaderInformation();
            var scanContents = new List<string>();
            foreach (int scanNumber in L)
            {
                var scan = Scan.FromFile(rawFile, scanNumber);
                var scanStatistics = rawFile.GetScanStatsForScanNumber(scanNumber);
                var centroidStream = rawFile.GetCentroidStream(scanNumber, false);
                var scanEvent = rawFile.GetScanEventForScanNumber(scanNumber);
                var scanTrailer = rawFile.GetTrailerExtraInformation(scanNumber);

                ScanTrailerDict = new Dictionary<string, string>();
                foreach (var (key, value) in Enumerable.Range(0, scanTrailer.Length).Select(i => (scanTrailer.Labels[i], scanTrailer.Values[i])))
                { ScanTrailerDict[key] = value.Trim(); }

                if (!int.TryParse(ScanTrailerDict.GetValueOrDefault("Charge State:", "-1"), out charge))
                {
                    charge = -1;
                }

                scanContents.Add($"e$Spectrum[[{count++}]] <- list(");
                scanContents.Add($"\tscan = {scanNumber},");
                scanContents.Add($"\tStartTime = {scanStatistics.StartTime},");
                scanContents.Add($"\trtinseconds = {Math.Round(scanStatistics.StartTime * 60 * 1000) / 1000},");
                scanContents.Add($"\tcharge = {(charge > 0 ? charge.ToString() : "NA")},");
                double precursorMass;
                try
                {
                    var reaction0 = scanEvent.GetReaction(0);
                    precursorMass = reaction0.PrecursorMass;
                }
                catch
                {
                    precursorMass = -1;
                }
                scanContents.Add($"\tpepmass = {(precursorMass > 0 ? precursorMass.ToString() : "NA")},");

                string mzData = scanStatistics.IsCentroidScan && centroidStream.Length > 0
                    ? $"\tmZ = c({string.Join(", ", centroidStream.Masses)}),\n\tintensity = c({string.Join(", ", centroidStream.Intensities)})"
                    : "\tmZ = NULL,\n\tintensity = NULL";
                scanContents.Add(mzData);
                scanContents.Add("\t)");
                FileIOHelper.WriteTextToFile(filename, string.Join("\n", scanContents));
                scanContents.Clear(); // Clear contents out for next loop
            }
        }


        public static void WriteTrailerLabel(this IRawDataPlus rawFile)
        {
            rawFile.SelectInstrument(Device.MS, 1); // Get Mass Spectrometer data
            foreach (int scanNumber in Enumerable.Range(rawFile.RunHeaderEx.FirstSpectrum, rawFile.RunHeaderEx.LastSpectrum))
            {
                var scanTrailer = rawFile.GetTrailerExtraInformation(scanNumber);
                Console.WriteLine(string.Join("\n", scanTrailer.Labels.ToArray()));
                return;
            }
        }

        public static void WriteTrailerValues(this IRawDataPlus rawFile, string label)
        {
            rawFile.SelectInstrument(Device.MS, 1); // Get Mass Spectrometer data
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

        /// <summary>
        /// </summary>
        /// <param name="rawFile"></param>
        /// <param name="filename"></param>
        /// <param name="L"></param>
        public static void WriteSpectrumAsRcode(this IRawDataPlus rawFile, string filename, List<int> L)
        {
            rawFile.SelectInstrument(Device.MS, 1); // Get Mass Spectrometer data
            int count = 1;
            int charge = -1;
            double monoIsotopicMz = -1;
            var trailerFields = rawFile.GetTrailerExtraHeaderInformation();
            Dictionary<string, string> ScanTrailerDict;

            foreach (int scanNumber in L)
            {
                var basepeakMass = -1.0;
                var basepeakIntensity = -1.0;

                var scanStatistics = rawFile.GetScanStatsForScanNumber(scanNumber);
                var centroidStream = rawFile.GetCentroidStream(scanNumber, false);
                var scanTrailer = rawFile.GetTrailerExtraInformation(scanNumber);
                var scanEvent = rawFile.GetScanEventForScanNumber(scanNumber);

                var scan = Scan.FromFile(rawFile, scanNumber);


                ScanTrailerDict = new Dictionary<string, string>();
                var spectrumContents = new List<string>();
                foreach (var (key, value) in Enumerable.Range(0, scanTrailer.Length).Select(i => (scanTrailer.Labels[i], scanTrailer.Values[i])))
                { ScanTrailerDict[key] = value.Trim(); }

                if (!int.TryParse(ScanTrailerDict.GetValueOrDefault("Charge State:", "-1"), out charge))
                {
                    charge = -1;
                }

                if (!double.TryParse(ScanTrailerDict.GetValueOrDefault("Monoisotopic M/Z:", "-1"), out monoIsotopicMz))
                {
                    monoIsotopicMz = -1.0;
                }

                spectrumContents.Add($"e$Spectrum[[{count++}]] <- list(");
                spectrumContents.Add($"\tscan = {scanNumber},");

                try
                {
                    basepeakMass = (scanStatistics.BasePeakMass);
                    basepeakIntensity = Math.Round(scanStatistics.BasePeakIntensity);
                    spectrumContents.Add($"\tbasePeak = c({basepeakMass}, {basepeakIntensity}),");
                }
                catch
                {
                    spectrumContents.Add("\tbasePeak = c(NA, NA),");
                }
                spectrumContents.Add($"\tTIC = {scanStatistics.TIC},");
                spectrumContents.Add($"\tmassRange = c({scanStatistics.LowMass}, {scanStatistics.HighMass}),");
                spectrumContents.Add($"\tscanType = \"{scanStatistics.ScanType}\",");
                spectrumContents.Add($"\tStartTime = {scanStatistics.StartTime},");
                spectrumContents.Add($"\trtinseconds = {Math.Round(scanStatistics.StartTime * 60 * 1000) / 1000},");
                try
                {
                    var reaction0 = scanEvent.GetReaction(0);
                    spectrumContents.Add($"\tpepmass = {reaction0.PrecursorMass},");
                }
                catch
                {
                    spectrumContents.Add("\tpepmass = NA,");
                }

                if (scanStatistics.IsCentroidScan && centroidStream.Length > 0)
                {
                    // Get the centroid (label) data from the RAW file for this scan
                    spectrumContents.Add("\tcentroidStream = TRUE,");

                    spectrumContents.Add($"\tHasCentroidStream = '{scan.HasCentroidStream}, Length={scan.CentroidScan.Length}',");
                    if (scan.HasCentroidStream)
                    {
                        spectrumContents.Add("\tcentroid.mZ = c(" + string.Join(", ", scan.CentroidScan.Masses.ToArray()) + "),");
                        spectrumContents.Add("\tcentroid.intensity = c(" + string.Join(", ", scan.CentroidScan.Intensities.ToArray()) + "),");
                    }

                    spectrumContents.Add($"\ttitle = \"File: {Path.GetFileName(rawFile.FileName)}; SpectrumID: {null}; scans: {scanNumber}\",");

                    if (monoIsotopicMz > 0)
                        spectrumContents.Add($"\tmonoisotopicMz = {monoIsotopicMz},");
                    else
                        spectrumContents.Add("\tmonoisotopicMz = NA,");


                    if (charge > 0)
                    {
                        spectrumContents.Add($"\tcharge = {charge},");
                    }
                    else
                    {
                        spectrumContents.Add("\tcharge = NA,");
                    }


                    spectrumContents.Add("\tmZ = c(" + string.Join(", ", centroidStream.Masses) + "),");
                    spectrumContents.Add("\tintensity = c(" + string.Join(", ", centroidStream.Intensities) + "),");
                    spectrumContents.Add("\tnoises = c(" + string.Join(", ", centroidStream.Noises) + "),");
                    spectrumContents.Add("\tresolutions = c(" + string.Join(", ", centroidStream.Resolutions.ToArray()) + "),");
                    spectrumContents.Add("\tcharges = c(" + string.Join(", ", centroidStream.Charges) + "),");
                    spectrumContents.Add("\tbaselines = c(" + string.Join(", ", centroidStream.Baselines) + "),");

                }
                else
                {
                    spectrumContents.Add("\tcentroidStream = FALSE,");

                    spectrumContents.Add($"\tHasCentroidStream = '{scan.HasCentroidStream}, Length={scan.CentroidScan.Length}',");
                    if (scan.HasCentroidStream)
                    {
                        spectrumContents.Add("\tcentroid.mZ = c(" + string.Join(",", scan.CentroidScan.Masses.ToArray()) + "),");
                        spectrumContents.Add("\tcentroid.intensity = c(" + string.Join(",", scan.CentroidScan.Intensities.ToArray()) + "),");

                        // https://github.com/compomics/ThermoRawFileParser/blob/c293d4aa1b04bfd62124ff42c512572427a4316a/Writer/MzMlSpectrumWriter.cs#L1664
                        spectrumContents.Add($"\tcentroid.PreferredNoises = c({string.Join(", ", scan.PreferredNoises.ToArray())}),");
                        spectrumContents.Add($"\tcentroid.PreferredMasses = c({string.Join(", ", scan.PreferredMasses.ToArray())}),");
                        //Console.WriteLine("\tcentroid.PreferredBaselines = c({0}),", string.Join(", ", scan.PreferredBaselines.ToArray()));
                    }

                    spectrumContents.Add($"\ttitle = \"File: {Path.GetFileName(rawFile.FileName)}; SpectrumID: {null}; scans: {scanNumber}\",");

                    if (charge > 0)
                        spectrumContents.Add($"\tcharge = {charge},");
                    else
                        spectrumContents.Add("\tcharge = NA,");

                    if (monoIsotopicMz > 0)
                        spectrumContents.Add($"\tmonoisotopicMz = {monoIsotopicMz},");
                    else
                        spectrumContents.Add("\tmonoisotopicMz = NA,");

                    spectrumContents.Add("\tmZ = c(" + string.Join(",", scan.SegmentedScan.Positions) + "),");
                    spectrumContents.Add("\tintensity = c(" + string.Join(",", scan.SegmentedScan.Intensities) + "),");
                    // spectrumContents.Add("\tnoises = c(" + string.Join(",", scan.SegmentedScan.Noises) + "),");
                }
                // ============= Instrument Data =============
                // write scan Trailer
                var trailerValues = scanTrailer.Values;
                var trailerLabels = scanTrailer.Labels;
                var zipTrailer = trailerLabels.ToArray().Zip(trailerValues, (a, b) => string.Format("\t\"{0}\" = \"{1}\"", a, b));
                spectrumContents.Add(string.Join(", \n", zipTrailer));
                spectrumContents.Add(")");
                FileIOHelper.WriteTextToFile(filename, string.Join("\n", spectrumContents));
            }


            return;
        }
        /// <summary>
        /// Extracts LC gradient information from the instrument method
        /// </summary>
        public static void ExtractLCGradient(this IRawDataPlus rawFile, string filename)
        {
            try
            {
                rawFile.SelectInstrument(Device.MS, 1); // Get Mass Spectrometer data
                // Get the instrument method - typically instrument 0 is the LC
                var instrumentMethod = rawFile.GetInstrumentMethod(0);
                var gradientFileContents = new List<string>();

                gradientFileContents.Add("#R\n");
                gradientFileContents.Add("e$gradient <- list()\n");

                // The instrument method is typically in XML or plain text format
                // Parse the method string to extract gradient information
                var methodText = instrumentMethod.ToString();

                // Use regex patterns to extract gradient table information
                var timestampPattern = @"Time\s*[:=]\s*([\d.]+)";
                var percentBPattern = @"%B\s*[:=]\s*([\d.]+)";
                var flowRatePattern = @"Flow\s*[:=]\s*([\d.]+)";
                var curvePattern = @"Curve\s*[:=]\s*(\d+)";

                var lines = methodText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                int gradientIndex = 1;

                foreach (var line in lines)
                {
                    var timeMatch = System.Text.RegularExpressions.Regex.Match(line, timestampPattern);
                    var percentBMatch = System.Text.RegularExpressions.Regex.Match(line, percentBPattern);
                    var flowMatch = System.Text.RegularExpressions.Regex.Match(line, flowRatePattern);
                    var curveMatch = System.Text.RegularExpressions.Regex.Match(line, curvePattern);

                    if (timeMatch.Success || percentBMatch.Success || flowMatch.Success)
                    {
                        gradientFileContents.Add($"e$gradient[[{gradientIndex}]] <- list(");

                        if (timeMatch.Success)
                            gradientFileContents.Add($"\ttimestamp = {timeMatch.Groups[1].Value},");
                        else
                            gradientFileContents.Add("\ttimestamp = NA,");

                        if (percentBMatch.Success)
                            gradientFileContents.Add($"\tpercentB = {percentBMatch.Groups[1].Value},");
                        else
                            gradientFileContents.Add("\tpercentB = NA,");

                        if (flowMatch.Success)
                            gradientFileContents.Add($"\tflowRate = {flowMatch.Groups[1].Value},");
                        else
                            gradientFileContents.Add("\tflowRate = NA,");

                        if (curveMatch.Success)
                            gradientFileContents.Add($"\tcurve = {curveMatch.Groups[1].Value}");
                        else
                            gradientFileContents.Add("\tcurve = NA");

                        gradientFileContents.Add(")");
                        gradientIndex++;
                    }
                }

                gradientFileContents.Add($"\ne$gradient$length <- {gradientIndex - 1}");
                FileIOHelper.WriteTextToFile(filename, string.Join("\n", gradientFileContents));
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error extracting LC gradient: {ex.Message}");
            }
        }

        public static void GetTuneLogs(this IRawDataPlus rawFile)
        {
            rawFile.SelectInstrument(Device.MS, 1); // Get Mass Spectrometer data
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
        public static void ExtractLCPressureFromAnalogToDigitalCard(this IRawDataPlus rawFile, string outputFileName)
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
    } // end IRawDataPlusExtension class
} // end FGCZExtensions namespace

namespace FGCZ_Raw
{
    using FGCZExtensions;

    internal static class Program
    {
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
        private static void GetChromatogram(IRawDataPlus rawFile, int startScan, int endScan, string filename, string filter = "ms")
        {
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

        private static void ExtractIonChromatogramAsRcode(IRawDataPlus rawFile, int startScan, int endScan, List<double> massList,
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

        private static void ExtractSampleAndMethodFromSequenceFile(ISequenceFileAccess seqFile)
        {
            Console.WriteLine("# Sample and Method information from the sequence file:");
            int sampleCount = seqFile.Samples.Count;
            for (int i = 0; i < sampleCount; i++)
            {
                var sample = seqFile.Samples[i];
                Console.WriteLine("# Sample {0}: SampleId={1}, SampleName={2}, SampleType={3}, Comment={4}, Vial={5}, InjectionVolume={6}, Barcode={7}, BarcodeStatus={8}, CalibrationLevel={9}, DilutionFactor={10}, InstrumentMethodFile={11}, RawFileName={12}, CalibrationFile={13}, IstdAmount={14}, RowNumber={15}, Path={16}, ProcessingMethodFile={17}, SampleVolume={18}, SampleWeight={19}, UserText={20}",
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

        private static void ExtractInformationFromMethodFile(IInstrumentMethodFileAccess methodFile)
        {
            var devices = methodFile.Devices;
            Console.WriteLine($"Type of devices: {devices.GetType()}");
            foreach (var deviceEntry in devices)
            {
                Console.WriteLine($"Device Key: {deviceEntry.Key}");
                Console.WriteLine($"MethodText: {deviceEntry.Value.MethodText}");
            }
        }

        private static void Main(string[] args)
        {
            Argument<string> inputRawFileArg = new Argument<string>("inputfile")
            {
                Description = "Input RAW file"
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
            // `gradient` command and arguments
            Command gradientCommand = new Command("gradient", "Extracts LC gradient information from the instrument method as R code to a file.");
            gradientCommand.Arguments.Add(inputRawFileArg);
            gradientCommand.Arguments.Add(outputFileArg);
            gradientCommand.SetAction((ParseResult parseResult) =>
            {
                string inputFile = parseResult.GetValue(inputRawFileArg);
                string outputFile = parseResult.GetValue(outputFileArg);
                using (var rawFile = FileIOHelper.CreateRawDataPlus(inputFile))
                {
                    rawFile.ExtractLCGradient(outputFile);
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
            // NOTE: We'll need to figure out how to properly set the default value for output file since it will inherit value from root command
            // chromatogramOutputFileArg.SetDefaultValue("chromatogram.csv");
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
                string outputfile = parseResult.GetValue(outputFileArg);
                string filterstring = parseResult.GetValue(chromatogramFilterStringArg);
                using (var rawFile = FileIOHelper.CreateRawDataPlus(inputfile))
                {
                    var firstScanNumber = rawFile.RunHeaderEx.FirstSpectrum;
                    var lastScanNumber = rawFile.RunHeaderEx.LastSpectrum;
                    GetChromatogram(rawFile, firstScanNumber, lastScanNumber, outputfile, filterstring);
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
            Command scansCommand = new Command("scans", "Extracts scans (spectra) of a given ID as Rcode.");
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
                        rawFile.WriteSpectrumAsRcode0(outputfile);
                    }
                    else
                    {
                        rawFile.WriteSpectrumAsRcode(outputfile, scanIds);
                    }
                }
            });
            Argument<string> bareboneScanFileArg = new Argument<string>("scanfile")
            {
                Description = "File containing scan ids to extract."
            };
            Command bareboneCommand = new Command("barebone", "Extracts only the spectra of a given ID as Rcode.");
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
                        rawFile.WriteSpectrumAsRcode0(outputfile);
                    }
                    else
                    {
                        rawFile.WriteCentroidSpectrumAsRcode(outputfile, scanIds);
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
                    ExtractIonChromatogramAsRcode(rawFile, -1, -1, massList, ppmError, outputfile);
                }
            });
            // `getTuneLogs` command and arguments
            var getTuneLogsCommand = new Command("getTuneLogs", "Retrieves the tune information from the raw file.");
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
            extractSampleMethodCommand.SetAction((ParseResult parseResult) =>
            {
                string inputfile = parseResult.GetValue(inputRawFileArg);
                var seqFile = FileIOHelper.CreateSequenceFile(inputfile);
                ExtractSampleAndMethodFromSequenceFile(seqFile);
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
                    rawFile.ExtractLCPressureFromAnalogToDigitalCard(outputFile);
                }
            });
            // `extract-method-info` command and arguments
            Command extractMethodInfoCommand = new Command("extract-method-info", "Extracts method information from an instrument method file.");
            extractMethodInfoCommand.Arguments.Add(inputRawFileArg);
            extractMethodInfoCommand.SetAction((ParseResult parseResult) =>
            {
                string inputfile = parseResult.GetValue(inputRawFileArg);
                var methodFile = FileIOHelper.CreateInstrumentMethodFile(inputfile);
                ExtractInformationFromMethodFile(methodFile);
            });
            // `version` option
            VersionOption versionOption = new VersionOption("--version")
            {
                Description = "Prints the rawrr version."
            };
            // Add the commands as sub-commands to the root command
            rootCommand.Subcommands.Add(headerRCommand);
            rootCommand.Subcommands.Add(gradientCommand);
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
