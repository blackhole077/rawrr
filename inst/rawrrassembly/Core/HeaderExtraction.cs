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
    public static partial class IRawDataPlusExtension
    {
        public static List<(string key, object value)> ExtractInstrumentData(this IRawDataPlus rawFile, Device device = Device.MS, int deviceNumber = 1)
        {
            rawFile.SelectInstrument(device, deviceNumber);
            var instrumentData = rawFile.GetInstrumentData();
            List<(string key, object value)> instrumentInfoEntries = new List<(string key, object value)>();
            // Instrument information
            instrumentInfoEntries.Add(("Instrument name", instrumentData.Name));
            instrumentInfoEntries.Add(("Instrument model", instrumentData.Model));
            instrumentInfoEntries.Add(("Serial number", instrumentData.SerialNumber));
            instrumentInfoEntries.Add(("Software version", instrumentData.SoftwareVersion));
            instrumentInfoEntries.Add(("Firmware version", instrumentData.HardwareVersion));
            instrumentInfoEntries.Add(("Units", instrumentData.Units));
            instrumentInfoEntries.Add(("Channel labels", string.Join(", ", instrumentData.ChannelLabels)));
            instrumentInfoEntries.Add(("Flags", instrumentData.Flags));
            instrumentInfoEntries.Add(("Axis label X", instrumentData.AxisLabelX));
            instrumentInfoEntries.Add(("Axis label Y", instrumentData.AxisLabelY));
            instrumentInfoEntries.Add(("Is valid", instrumentData.IsValid));
            return instrumentInfoEntries;
        }

        public static List<(string key, object value)> ExtractFileHeaderData(this IRawDataPlus rawFile)
        {
            IFileHeader fileHeader = rawFile.FileHeader;
            List<(string key, object value)> fileHeaderEntries = new List<(string key, object value)>();
            fileHeaderEntries.Add(("RAW File", Path.GetFileName(rawFile.FileName)));
            fileHeaderEntries.Add(("Creator ID", fileHeader.WhoCreatedId));
            fileHeaderEntries.Add(("Creator logon", fileHeader.WhoCreatedLogon));
            fileHeaderEntries.Add(("Modifier ID", fileHeader.WhoModifiedId));
            fileHeaderEntries.Add(("Modifier logon", fileHeader.WhoModifiedLogon));
            fileHeaderEntries.Add(("File type", fileHeader.FileType));
            fileHeaderEntries.Add(("Revision", fileHeader.Revision));
            fileHeaderEntries.Add(("Creation date", fileHeader.CreationDate.ToString("yyyy-MM-dd HH:mm:ss")));
            fileHeaderEntries.Add(("Modified date", fileHeader.ModifiedDate.ToString("yyyy-MM-dd HH:mm:ss")));
            fileHeaderEntries.Add(("Number of times modified", fileHeader.NumberOfTimesModified));
            fileHeaderEntries.Add(("Number of times calibrated", fileHeader.NumberOfTimesCalibrated));
            fileHeaderEntries.Add(("File description", fileHeader.FileDescription));
            return fileHeaderEntries;
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
        /// info$`Instrument methods`[[1]] &lt;- list(
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
        private static List<(string name, object value)> ExtractInstrumentMethods(IRawDataPlus rawFile, Device device = Device.MS, int deviceNumber = 1)
        {
            var instrumentMethodsList = new List<(string name, object value)>();
            try
            {
                rawFile.SelectInstrument(device, deviceNumber); // Get Mass Spectrometer data
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

        public static List<(string key, object value)> ExtractScanData(this IRawDataPlus rawFile, Device device = Device.MS, int deviceNumber = 1)
        {
            List<(string key, object value)> scanInfoEntries = new List<(string key, object value)>();
            if (device != Device.MS)
            {
                return scanInfoEntries;
            }
            IRunHeader runHeader = rawFile.RunHeaderEx;
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
            return scanInfoEntries;
        }
        private static List<(string key, object value)> ExtractSampleInfo(ISampleInformation sampleInfo)
        {
            List<(string key, object value)> sampleInfoEntries = new List<(string key, object value)>();
            sampleInfoEntries.Add(("Sample name", sampleInfo.SampleName));
            sampleInfoEntries.Add(("Sample id", sampleInfo.SampleId));
            sampleInfoEntries.Add(("Sample type", sampleInfo.SampleType));
            sampleInfoEntries.Add(("Sample comment", sampleInfo.Comment));
            sampleInfoEntries.Add(("Sample vial", sampleInfo.Vial));
            sampleInfoEntries.Add(("Sample volume", sampleInfo.SampleVolume));
            sampleInfoEntries.Add(("Sample injection volume", sampleInfo.InjectionVolume));
            sampleInfoEntries.Add(("Sample row number", sampleInfo.RowNumber));
            sampleInfoEntries.Add(("Sample dilution factor", sampleInfo.DilutionFactor));
            sampleInfoEntries.Add(("Sample barcode", sampleInfo.Barcode));
            for (int i = 0; i < sampleInfo.UserText.Length; i++)
            {
                string key = $"User text {i + 1}";
                string value = sampleInfo.UserText[i];
                sampleInfoEntries.Add((key, value));
            }
            return sampleInfoEntries;
        }

    }
}