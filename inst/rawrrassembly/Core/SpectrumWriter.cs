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
        public static void WriteSpectrumAsRCode0(this IRawDataPlus rawFile, string filename, Device device = Device.MS, int deviceNumber = 1)
        {
            rawFile.SelectInstrument(device, deviceNumber);
            int firstScanNumber = rawFile.RunHeaderEx.FirstSpectrum;
            int lastScanNumber = rawFile.RunHeaderEx.LastSpectrum;
            int charge = -1;
            double precursorMass = -1;
            Dictionary<string, string> ScanTrailerDict;
            List<List<(string key, object value)>> allSpectra = new List<List<(string key, object value)>>();
            foreach (int scanNumber in Enumerable.Range(firstScanNumber, lastScanNumber))
            {
                List<(string key, object value)> spectrumList = new List<(string key, object value)>();
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

                spectrumList.Add(($"scan", scanNumber));
                spectrumList.Add(($"scanType", scanStatistics.ScanType.ToString()));
                spectrumList.Add(($"StartTime", scanStatistics.StartTime));
                spectrumList.Add(($"rtinseconds", Math.Round(scanStatistics.StartTime * 60 * 1000) / 1000));
                spectrumList.Add(($"precursorMass", precursorMass));
                spectrumList.Add(($"MSOrder", scanFilter.MSOrder.ToString()));
                spectrumList.Add(($"charge", charge));
                allSpectra.Add(spectrumList);
            }
            var rcode = RCodeWriter.WriteRListOfLists("Spectrum", allSpectra);
            File.WriteAllText(filename, rcode);
        }

        /// <summary>
        /// </summary>
        /// <param name="rawFile"></param>
        /// <param name="filename"></param>
        /// <param name="L"></param>
        public static void WriteSpectrumAsRCode(this IRawDataPlus rawFile, string filename, List<int> scanIdList, Device device = Device.MS, int deviceNumber = 1)
        {
            rawFile.SelectInstrument(device, deviceNumber); // Get Mass Spectrometer data
            int charge = -1;
            double monoIsotopicMz = -1;
            var trailerFields = rawFile.GetTrailerExtraHeaderInformation();
            Dictionary<string, string> ScanTrailerDict;
            List<List<(string key, object value)>> allSpectra = new List<List<(string key, object value)>>();
            foreach (int scanNumber in scanIdList)
            {
                List<(string key, object value)> spectrumList = new List<(string key, object value)>();
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
                spectrumList.Add(("scan", scanNumber));
                try
                {
                    basepeakMass = (scanStatistics.BasePeakMass);
                    basepeakIntensity = Math.Round(scanStatistics.BasePeakIntensity);
                    spectrumList.Add(("basePeak", RVector<double>.Create(new double[] { basepeakMass, basepeakIntensity })));
                }
                catch
                {
                    spectrumList.Add(("basePeak", RVector<string>.Create(new string[] { "NA", "NA" })));
                }
                spectrumList.Add(("TIC", scanStatistics.TIC));
                spectrumList.Add(("massRange", RVector<double>.Create(new double[] { scanStatistics.LowMass, scanStatistics.HighMass })));
                spectrumList.Add(("scanType", scanStatistics.ScanType));
                spectrumList.Add(("StartTime", scanStatistics.StartTime));
                spectrumList.Add(("rtinseconds", Math.Round(scanStatistics.StartTime * 60 * 1000) / 1000));
                try
                {
                    var reaction0 = scanEvent.GetReaction(0);
                    spectrumList.Add(("pepmass", reaction0.PrecursorMass));
                }
                catch
                {
                    spectrumList.Add(("pepmass", "NA"));
                }

                if (scanStatistics.IsCentroidScan && centroidStream.Length > 0)
                {
                    // Get the centroid (label) data from the RAW file for this scan
                    spectrumList.Add(("centroidStream", "TRUE"));
                    spectrumList.Add(("HasCentroidStream", scan.HasCentroidStream));
                    spectrumList.Add(("Length", scan.CentroidScan.Length));
                    if (scan.HasCentroidStream)
                    {
                        spectrumList.Add(("centroid.mZ", RVector<double>.Create(scan.CentroidScan.Masses.ToArray())));
                        spectrumList.Add(("centroid.intensity", RVector<double>.Create(scan.CentroidScan.Intensities.ToArray())));
                    }

                    spectrumList.Add(("title", $"File: {Path.GetFileName(rawFile.FileName)}; SpectrumID: {null}; scans: {scanNumber}"));

                    if (monoIsotopicMz > 0)
                        spectrumList.Add(("monoisotopicMz", monoIsotopicMz));
                    else
                        spectrumList.Add(("monoisotopicMz", "NA"));


                    if (charge > 0)
                    {
                        spectrumList.Add(("charge", charge));
                    }
                    else
                    {
                        spectrumList.Add(("charge", "NA"));
                    }


                    spectrumList.Add(("mZ", RVector<double>.Create(centroidStream.Masses.ToArray())));
                    spectrumList.Add(("intensity", RVector<double>.Create(centroidStream.Intensities.ToArray())));
                    spectrumList.Add(("noises", RVector<double>.Create(centroidStream.Noises.ToArray())));
                    spectrumList.Add(("resolutions", RVector<double>.Create(centroidStream.Resolutions.ToArray())));
                    spectrumList.Add(("charges", RVector<double>.Create(centroidStream.Charges.ToArray())));
                    spectrumList.Add(("baselines", RVector<double>.Create(centroidStream.Baselines.ToArray())));

                }
                else
                {
                    spectrumList.Add(("centroidStream", "FALSE"));
                    spectrumList.Add(("HasCentroidStream", scan.HasCentroidStream));
                    spectrumList.Add(("Length", scan.CentroidScan.Length));
                    if (scan.HasCentroidStream)
                    {
                        spectrumList.Add(("centroid.mZ", RVector<double>.Create(scan.CentroidScan.Masses.ToArray())));
                        // spectrumList.Add(("centroid.mZ", $"c({string.Join(", ", scan.CentroidScan.Masses.ToArray())})"));
                        spectrumList.Add(("centroid.intensity", RVector<double>.Create(scan.CentroidScan.Intensities.ToArray())));

                        // https://github.com/compomics/ThermoRawFileParser/blob/c293d4aa1b04bfd62124ff42c512572427a4316a/Writer/MzMlSpectrumWriter.cs#L1664
                        spectrumList.Add(("centroid.PreferredNoises", RVector<double>.Create(scan.PreferredNoises.ToArray())));
                        spectrumList.Add(("centroid.PreferredMasses", RVector<double>.Create(scan.PreferredMasses.ToArray())));
                    }

                    spectrumList.Add(("title", $"File: {Path.GetFileName(rawFile.FileName)}; SpectrumID: {null}; scans: {scanNumber}"));
                    if (charge > 0)
                        spectrumList.Add(("charge", charge));
                    else
                        spectrumList.Add(("charge", "NA"));

                    if (monoIsotopicMz > 0)
                        spectrumList.Add(("monoisotopicMz", monoIsotopicMz));
                    else
                        spectrumList.Add(("monoisotopicMz", "NA"));

                    spectrumList.Add(("mZ", RVector<double>.Create(scan.SegmentedScan.Positions.ToArray())));
                    spectrumList.Add(("intensity", RVector<double>.Create(scan.SegmentedScan.Intensities.ToArray())));
                }
                // ============= Instrument Data =============
                // write scan Trailer
                var trailerValues = scanTrailer.Values;
                var trailerLabels = scanTrailer.Labels;
                var zipTrailer = trailerLabels.ToArray().Zip(trailerValues, (a, b) => string.Format("\t\"{0}\" = \"{1}\"", a.Trim(), b.Trim()));
                spectrumList.Add(("trailerValues", $"list({string.Join(", ", zipTrailer)})"));
                allSpectra.Add(spectrumList);
            }
            // Write out information we've gathered
            using (var file = FileIOHelper.CreateStreamWriter(filename))
            {
                string rCode = RCodeWriter.WriteRListOfLists("e$Spectrum", allSpectra);
                file.WriteLine(rCode);
            }
        }
        /// <summary>
        ///    implements
        ///    https://github.com/fgcz/rawrr/issues/43
        /// </summary>
        /// <param name="rawFile"></param>
        /// <param name="filename"></param>
        /// <param name="L"></param>
        public static void WriteCentroidSpectrumAsRCode(this IRawDataPlus rawFile, string filename, List<int> scanIdList, Device device = Device.MS, int deviceNumber = 1)
        {
            rawFile.SelectInstrument(device, deviceNumber);
            int charge = -1;
            Dictionary<string, string> ScanTrailerDict;
            var trailerFields = rawFile.GetTrailerExtraHeaderInformation();
            var scanContents = new List<string>();
            List<List<(string key, object value)>> allSpectra = new List<List<(string key, object value)>>();
            foreach (int scanNumber in scanIdList)
            {
                List<(string key, object value)> spectrumList = new List<(string key, object value)>();
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

                spectrumList.Add(("scan", scanNumber));
                spectrumList.Add(("StartTime", scanStatistics.StartTime));
                spectrumList.Add(("rTimeInSeconds", Math.Round(scanStatistics.StartTime * 60 * 1000) / 1000));
                spectrumList.Add(("charge", (charge > 0 ? charge.ToString() : "NA")));
                spectrumList.Add(("pepmass", precursorMass > 0 ? precursorMass.ToString() : "NA"));
                if (scanStatistics.IsCentroidScan && centroidStream.Length > 0)
                {
                    spectrumList.Add(("mZ", RVector<double>.Create(centroidStream.Masses.ToArray())));
                    spectrumList.Add(("intensity", RVector<double>.Create(centroidStream.Intensities.ToArray())));
                }
                else
                {
                    spectrumList.Add(("mZ", "NULL"));
                    spectrumList.Add(("intensity", "NULL"));
                }
                allSpectra.Add(spectrumList);
            }
            var rCode = RCodeWriter.WriteRListOfLists("CentroidSpectrum", allSpectra);
            File.WriteAllText(filename, rCode);
        }
    } // Class IRawDataPlusExtension
} // Namespace FGCZExtensions