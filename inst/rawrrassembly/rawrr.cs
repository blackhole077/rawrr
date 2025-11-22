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
            return(s.Replace(" ", "")
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
                List<string> instrumentMethods = WriteInstrumentMethods(file, rawFile);

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
                FileIOHelper.WriteInfoLines(file, fileInfoEntries);
                FileIOHelper.WriteInfoLines(file, instrumentInfoEntries);
                file.WriteLine(string.Join("", instrumentMethods));
                FileIOHelper.WriteInfoLines(file, scanInfoEntries);

                FileIOHelper.WriteInfoLines(file, GenerateSampleInfo(file, sampleInfo));

                // User text
                FileIOHelper.WriteUserText(file, sampleInfo.UserText);
            }
        }

        private static List<(string key, object value)> GenerateSampleInfo(StreamWriter file, ISampleInformation sampleInfo)
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
        private static List<string> WriteInstrumentMethods(StreamWriter file, IRawDataPlus rawFile)
        {
            var instrumentMethods = new List<string>();
            try
            {
                // Get all instrument friendly names from the instrument method
                var instrumentFriendlyNames = rawFile.GetAllInstrumentFriendlyNamesFromInstrumentMethod();

                // Get the number of instrument methods
                int methodCount = rawFile.InstrumentMethodsCount;

                instrumentMethods.Add($"e$info$`Number of instrument methods` <- {methodCount}\n");
                instrumentMethods.Add("e$info$`Instrument methods` <- list()\n");

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

                    instrumentMethods.Add($"e$info$`Instrument methods`[[{i + 1}]] <- list(\n\tname = '{instrumentName}',\n\tmethod = '{methodContent}'\n)\n");
                }
            }
            catch (Exception ex)
            {
                // If method extraction fails, log it but don't crash
                instrumentMethods.Add($"# Warning: Could not extract instrument methods: {ex.Message}\n");
            }
            return instrumentMethods;
        }

        public static void GetIndex(this IRawDataPlus rawFile){
            int firstScanNumber = rawFile.RunHeaderEx.FirstSpectrum;
            int lastScanNumber = rawFile.RunHeaderEx.LastSpectrum;

            double charge, precursorMass;
            double monoIsotopicMz;
            int masterScan, dependencyType;

            Console.WriteLine("scan;scanType;StartTime;precursorMass;MSOrder;charge;masterScan;dependencyType;monoisotopicMz");

            Dictionary<string, string> ScanTrailerDict;

            foreach (int scanNumber in Enumerable.Range(firstScanNumber, lastScanNumber)){
                var scanTrailer = rawFile.GetTrailerExtraInformation(scanNumber);
                var scanStatistics = rawFile.GetScanStatsForScanNumber(scanNumber);
                var scanEvent = rawFile.GetScanEventForScanNumber(scanNumber);
                var scanFilter = rawFile.GetFilterForScanNumber(scanNumber);

                // TODO(cpanse): implement a public class ScanTrailer
                ScanTrailerDict = new Dictionary<string, string>();
                foreach (var (key, value) in Enumerable.Range(0, scanTrailer.Length).Select(i => (scanTrailer.Labels[i], scanTrailer.Values[i])))
                { ScanTrailerDict[key] = value.Trim(); }

                try{
                    var reaction0 = scanEvent.GetReaction(0);
                    precursorMass =  reaction0.PrecursorMass;
                } catch{
                    precursorMass = -1;
                }

                try{
                    charge = int.Parse(ScanTrailerDict["Charge State:"]);
                } catch {
                    charge = -1;
                }

                try{
                    masterScan = int.Parse(ScanTrailerDict["Master Scan Number:"]);
                } catch {
                    masterScan= -1;
                }

                try{
                    dependencyType = int.Parse(ScanTrailerDict["Dependency Type:"]);
                } catch {
                    dependencyType = -1;
                }

                try{
                    monoIsotopicMz = Convert.ToDouble(ScanTrailerDict["Monoisotopic M/Z:"]);
                } catch {
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
            int firstScanNumber = rawFile.RunHeaderEx.FirstSpectrum;
            int lastScanNumber = rawFile.RunHeaderEx.LastSpectrum;
            int charge = -1;
            double precursorMass=-1;
            Dictionary<string, string> ScanTrailerDict;
            List<string> spectrumFileContents = new List<string>();
            foreach (int scanNumber in Enumerable.Range(firstScanNumber, lastScanNumber)){
                    var scanTrailer = rawFile.GetTrailerExtraInformation(scanNumber);
                    var scanStatistics = rawFile.GetScanStatsForScanNumber(scanNumber);
                    var scanEvent = rawFile.GetScanEventForScanNumber(scanNumber);
                    var scanFilter = rawFile.GetFilterForScanNumber(scanNumber);

                    ScanTrailerDict = new Dictionary<string, string>();
                    foreach (var (key, value) in Enumerable.Range(0, scanTrailer.Length).Select(i => (scanTrailer.Labels[i], scanTrailer.Values[i])))
                    { ScanTrailerDict[key] = value.Trim(); }


                    try{
                        var reaction0 = scanEvent.GetReaction(0);
                        precursorMass =  reaction0.PrecursorMass;
                    }
                    catch{
                        precursorMass = -1;
                    }

                    try{
                        charge = int.Parse(ScanTrailerDict["Charge State:"]);
                    }
                    catch {
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
                try{
                    var reaction0 = scanEvent.GetReaction(0);
                    precursorMass = reaction0.PrecursorMass;
                }catch{
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
            foreach (int scanNumber in Enumerable.Range(rawFile.RunHeaderEx.FirstSpectrum, rawFile.RunHeaderEx.LastSpectrum))
            {
                var scanTrailer = rawFile.GetTrailerExtraInformation(scanNumber);
                Console.WriteLine(string.Join("\n", scanTrailer.Labels.ToArray()));
                return;
            }
        }

        public static void WriteTrailerValues(this IRawDataPlus rawFile, string label)
        {
            Dictionary<string, string> ScanTrailerDict;
            foreach (int scanNumber in Enumerable.Range(rawFile.RunHeaderEx.FirstSpectrum, rawFile.RunHeaderEx.LastSpectrum))
            {
                var scanTrailer = rawFile.GetTrailerExtraInformation(scanNumber);

                // TODO(cpanse): implement a public class ScanTrailer
                ScanTrailerDict = new Dictionary<string, string>();
                foreach (var (key, value) in Enumerable.Range(0, scanTrailer.Length).Select(i => (scanTrailer.Labels[i], scanTrailer.Values[i])))
                { ScanTrailerDict[key] = value.Trim(); }

                if (ScanTrailerDict.ContainsKey(label)){
                    Console.WriteLine(ScanTrailerDict[label]);
                }else{
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

                try{
                    charge = int.Parse(ScanTrailerDict["Charge State:"]);
                } catch {
                    charge = -1;
                }

                try{
                    monoIsotopicMz = Convert.ToDouble(ScanTrailerDict["Monoisotopic M/Z:"]);
                } catch {
                    monoIsotopicMz = -1.0;
                }

                spectrumContents.Add($"e$Spectrum[[{count++}]] <- list(");
                spectrumContents.Add($"\tscan = {scanNumber},");

                try
                {
                    basepeakMass =  (scanStatistics.BasePeakMass);
                    basepeakIntensity =  Math.Round(scanStatistics.BasePeakIntensity);
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
                    if(scan.HasCentroidStream){
                        spectrumContents.Add("\tcentroid.mZ = c(" + string.Join(", ", scan.CentroidScan.Masses.ToArray()) + "),");
                        spectrumContents.Add("\tcentroid.intensity = c(" + string.Join(", ", scan.CentroidScan.Intensities.ToArray()) + "),");
                    }

                    spectrumContents.Add($"\ttitle = \"File: {Path.GetFileName(rawFile.FileName)}; SpectrumID: {null}; scans: {scanNumber}\",");

                    if (monoIsotopicMz > 0)
                    spectrumContents.Add($"\tmonoisotopicMz = {monoIsotopicMz},");
                    else
                    spectrumContents.Add("\tmonoisotopicMz = NA,");


                    if (charge > 0){
                        spectrumContents.Add($"\tcharge = {charge},");
                    }
                    else{
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
                    if(scan.HasCentroidStream){
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
    } // end IRawDataPlusExtension class
} // end FGCZExtensions namespace

namespace FGCZ_Raw
{
    using FGCZExtensions;

    internal static class Program
    {
        private static void Main(string[] args)
        {
            // This local variable controls if the AnalyzeAllScans method is called
            // bool analyzeScans = false;
            const string rawrrVersion = "1.17.2";
            string filename = string.Empty;
            string mode = string.Empty;
            string filterString = string.Empty;
            Hashtable hashtable = new Hashtable()
            {
                {"filter", "List all scan ids pass the filter string (option 2)."},
                {"getFilters", "List all scan filters of a given raw file."},
                {"isValidFilter", "Checks whether the provided argument string (option 2) is a valid filter."},
                {"headerR", "Writes the raw file's meta data as R code to a file."},
                {"gradient", "Extracts LC gradient information from the instrument method as R code to a file."},
                {"chromatogram", "Extracts base peak and total ion count chromatograms into a file."},
                {
                    "xic",
                    "Extracts filtered (option 2) ion chromatograms within a given mass and mass tolerance [in ppm] (option 3) xic of a given raw file as R code into a file."
                },
                {"scans", "Extracts scans (spectra) of a given ID as Rcode."},
                {"barebone", "Extracts 'barebone' scans (spectra), including only mZ, intensity , precursorMass, rtinsecodonds and charge state, of a given ID as Rcode."},
                {"index", "Prints index as csv of all scans."},
                {"trailer", "Prints all trailer labels."}
            };
            var helpOptions = new List<string>() {"help", "--help", "-h", "h", "/h"};
            var versionOptions = new List<string>() {"version", "--version", "-v", "-V", "/v"};

            if (args.Length >= 2){
                filename = args[0];
                mode = args[1];
                if (!hashtable.Contains(mode))
                {
                    Console.WriteLine("\nOption '{0}' is not defined. Please use one of the following options as argument:", mode);
                    foreach (var k in hashtable.Keys)
                    Console.WriteLine("  {0,-15}   {1}", k.ToString(), hashtable[k].ToString());
                    Console.WriteLine();

                    Environment.Exit(1);
                }}
                else
                {
                    if (args.Length == 0)
                    {
                        Console.WriteLine("No RAW file specified!");
                        return;
                    }
                    else if (versionOptions.Contains(args[0]))
                    {
                        Console.WriteLine(rawrrVersion);
                        Environment.Exit(0);
                    }
                    else if (helpOptions.Contains(args[0]))
                    {
                        Console.WriteLine("\nUsage:\n");
                        Console.WriteLine("  rawrr.exe <raw file> <option>\n");
                        Console.WriteLine("  rawrr.exe <raw file> <option> <input file> <output file>\n");
                        Console.WriteLine(
                        "  rawrr.exe <raw file> <option 1> <option 2> <option 3> <input file> <output file>\n");
                        Console.WriteLine("\nOptions:\n");
                        foreach (var k in hashtable.Keys)
                        {
                            Console.WriteLine("  {0,-15}   {1}", k.ToString(), hashtable[k].ToString());
                        }
                        Console.WriteLine("\nReport bugs at <https://github.com/fgcz/rawrr/issues>.\n");
                        Environment.Exit(0);
                    }
                    else
                    {
                        Console.WriteLine("run 'rawrr.exe help'.");
                        Environment.Exit(1);
                    }
                }

                if (string.IsNullOrEmpty(filename))
                {
                    Console.WriteLine("No RAW file specified!");
                    return;
                }
                //Environment.Exit(0);

                // Get the memory used at the beginning of processing
                Process processBefore = Process.GetCurrentProcess();
                long memoryBefore = processBefore.PrivateMemorySize64 / 1024;
                try
                {
                    // Create the IRawDataPlus object for accessing the RAW file
                    var rawFile = RawFileReaderAdapter.FileFactory(filename);

                    if (!rawFile.IsOpen || rawFile.IsError)
                    {
                        Console.WriteLine("Unable to access the RAW file using the RawFileReader class!");

                        return;
                    }

                    // Check for any errors in the RAW file
                    if (rawFile.IsError)
                    {
                        Console.WriteLine("Error opening ({0}) - {1}", rawFile.FileError, filename);

                        return;
                    }

                    // Check if the RAW file is being acquired
                    if (rawFile.InAcquisition)
                    {
                        Console.WriteLine("RAW file still being acquired - " + filename);

                        return;
                    }

                    rawFile.SelectInstrument(Device.MS, 1);
                    //Console.WriteLine("DEBUG {0}", rawFile.GetInstrumentMethod(3).ToString());

                    // Get the first and last scan from the RAW file
                    int firstScanNumber = rawFile.RunHeaderEx.FirstSpectrum;
                    int lastScanNumber = rawFile.RunHeaderEx.LastSpectrum;

                    // Get the start and end time from the RAW file
                    double startTime = rawFile.RunHeaderEx.StartTime;
                    double endTime = rawFile.RunHeaderEx.EndTime;

                    if (mode == "headerR"){
                        var outputFilename = args[3];
                        rawFile.GenerateHeaderInformationAsRCode(outputFilename);
                        return;
                    }
                    else if (mode == "gradient")
                    {
                        var outputFilename = args[2];
                        rawFile.ExtractLCGradient(outputFilename);
                        return;
                    }

                    // Get the number of filters present in the RAW file
                    int numberFilters = rawFile.GetFilters().Count;

                    if (mode == "trailer" && args.Length == 2){
                        rawFile.WriteTrailerLabel();
                        return;
                    } else if (mode == "trailer" && args.Length == 3) {
                        //Console.WriteLine(args[2]);
                        rawFile.WriteTrailerValues(args[2]);
                        return;
                    }

                    if (mode == "filter")
                    {
                        filterString = args[2].ToString();
                        int precision = int.Parse(args[3]);
                        var outputFilename = args[4];

                        if(!IsValidFilter(rawFile, filterString))
                        Environment.Exit(1);

                        using (var file = FileIOHelper.CreateStreamWriter(outputFilename))
                        {
                            foreach (var ss in rawFile
                            .GetFilteredScanEnumerator(rawFile.GetFilterFromString(filterString, precision)).ToArray())
                            {
                                file.WriteLine(ss);
                            }
                        }

                        Environment.Exit(0);
                    }

                    if (mode == "isValidFilter")
                    {
                        Console.WriteLine(IsValidFilter(rawFile, args[2].ToString()).ToString());
                        Environment.Exit(0);
                    }

                    if (mode == "getFilters")
                    {
                        foreach (var filter in rawFile.GetFilters())
                        {
                            Console.WriteLine(filter.ToString());
                        }
                        Environment.Exit(0);
                    }

                    if (mode == "chromatogram")
                    {
                        // Get the BasePeak chromatogram for the MS data
                        string filter = "ms";
                        string outputcsv = "chromatogram.csv";
                        try {
                            filter = args[2];
                        }
                        catch{
                        }
                        try {
                            outputcsv = args[3];
                        }
                        catch{
                        }
                        GetChromatogram(rawFile, firstScanNumber, lastScanNumber, outputcsv, filter);
                        Environment.Exit(0);
                    }


                    if (mode == "index"){
                        rawFile.GetIndex();
                        return;
                    }

                    if (mode == "scans")
                    {
                        List<int> scans = new List<int>();

                        var scanfile = args[2];
                        int scanNumber;

                        foreach (var line in File.ReadAllLines(scanfile))
                        {

                            try{
                                Int32.TryParse(line, out scanNumber);
                                if (scanNumber > 0)
                                scans.Add(scanNumber);
                            }
                            catch{}
                        }

                        if (scans.Count == 0)
                        rawFile.WriteSpectrumAsRcode0(args[3]);
                        else
                        rawFile.WriteSpectrumAsRcode(args[3], scans);

                        return;

                    }
                    // extracs only the specta
                    if (mode == "barebone")
                    {
                        List<int> scans = new List<int>();

                        var scanfile = args[2];
                        int scanNumber;

                        foreach (var line in File.ReadAllLines(scanfile))
                        {

                            // parses the input while accepting only integers greater than 0
                            try{
                                Int32.TryParse(line, out scanNumber);
                                if (scanNumber > 0)
                                scans.Add(scanNumber);
                            }
                            catch{}
                        }

                        if (scans.Count == 0)
                        rawFile.WriteSpectrumAsRcode0(args[3]);
                        else
                        rawFile.WriteCentroidSpectrumAsRcode(args[3], scans);

                        return;

                    }

                    if (mode == "xic")
                    {
                        //  Console.WriteLine("xic");
                        try
                        {
                            double ppmError = Convert.ToDouble(args[2]);
                            // Console.WriteLine(ppmError);
                            string filter = "ms";
                            try {
                                filter = args[3];
                            }
                            catch{
                            }
                            //Console.WriteLine(filter);
                            var inputFilename = args[4];
                            var outputFilename = args[5];

                            List<double> massList = new List<double>();
                            if (File.Exists(inputFilename))
                            {
                                foreach (var line in File.ReadAllLines(inputFilename))
                                {
                                    //Console.WriteLine(Convert.ToDouble(line));
                                    massList.Add(Convert.ToDouble(line));
                                }

                                ExtractIonChromatogramAsRcode(rawFile, -1, -1, massList, ppmError, outputFilename, filter);
                            }

                            return;
                        }
                        catch (Exception ex)
                        {
                            Console.Error.WriteLine("failed to catch configfile and itol");
                            Console.Error.WriteLine("{}", ex.Message);
                            return;
                        }
                    }
                }

                catch (Exception ex)
                {
                    Console.WriteLine("Error accessing RAWFileReader library! - " + ex.Message);
                }

                // Get the memory used at the end of processing
                Process processAfter = Process.GetCurrentProcess();
                long memoryAfter = processAfter.PrivateMemorySize64 / 1024;

                Console.WriteLine();
                Console.WriteLine("Memory Usage:");
                Console.WriteLine("   Before {0} kb, After {1} kb, Extra {2} kb", memoryBefore, memoryAfter,
                memoryAfter - memoryBefore);
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
            private static void GetChromatogram(IRawDataPlus rawFile, int startScan, int endScan,  string filename, string filter = "ms")
            {
                if (IsValidFilter(rawFile, filter) == false){
                    Console.WriteLine("# '{0}' is not a valid filter string.", filter);
                    return;
                }

                using (var file = FileIOHelper.CreateStreamWriter(filename))
                {
                    // TODO(tk@fgcz.ethz.ch): check mass interval for chromatograms and its dep for diff MS detector types
                    // TODO(cp@fgcz.ethz.ch): return mass intervals to the R environment
                    // Define the settings for getting the Base Peak chromatogram
                    ChromatogramTraceSettings settingsTIC = new ChromatogramTraceSettings(TraceType.TIC){Filter=filter};
                    ChromatogramTraceSettings settingsBasePeak = new ChromatogramTraceSettings(TraceType.BasePeak){
                        Filter=filter,
                        MassRanges = new[] {ThermoFisher.CommonCore.Data.Business.Range.Create(100, 1805)}
                    };
                    ChromatogramTraceSettings settingsMassRange = new ChromatogramTraceSettings(TraceType.MassRange){
                        Filter=filter,
                        MassRanges = new[] {ThermoFisher.CommonCore.Data.Business.Range.Create(50, 2000000)}
                    };

                    // Get the chromatogram from the RAW file.
                    var dataTIC = rawFile.GetChromatogramData(new IChromatogramSettings[] {settingsTIC}, startScan, endScan);
                    var dataMassRange = rawFile.GetChromatogramData(new IChromatogramSettings[] {settingsMassRange}, startScan, endScan);
                    var dataBasePeak = rawFile.GetChromatogramData(new IChromatogramSettings[] {settingsBasePeak}, startScan, endScan);

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
                if (rawFile.GetFilterFromString(filter) == null) {
                    return false;
                }
                return true;
            }

            private static void ExtractIonChromatogramAsRcode(IRawDataPlus rawFile, int startScan, int endScan, List<double> massList,
            double ppmError, string filename, string filter = "ms")
            {

                if (IsValidFilter(rawFile, filter) == false){
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
                        MassRanges = new[] {ThermoFisher.CommonCore.Data.Business.Range.Create(mass - massError, mass + massError)}
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
        }
    }
