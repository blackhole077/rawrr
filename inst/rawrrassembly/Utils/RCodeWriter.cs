/// <summary>
/// Provides utility methods for writing R code structures to output streams.
/// This class contains helper methods to generate R list syntax for various data structures.
/// </summary>
using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using ThermoFisher.CommonCore.Data.Interfaces;
using ThermoFisher.CommonCore.RawFileReader;
using ThermoFisher.CommonCore.Data.Business;

public static class RCodeWriter
{
    /// <summary>
    /// Generates R code for a list of lists structure and returns it as a string.
    /// This method creates an R list structure where each element is itself a list.
    /// The outer list is indexed using 1-based indexing as per R conventions.
    /// Each inner list is written using the WriteRList method.
    /// </summary>
    /// <param name="listName">The name of the R list variable to create.</param>
    /// <param name="listOfLists">A list of lists containing key-value pairs to be written as nested R lists.</param>
    public static string WriteRListOfLists(string listName, List<List<(string key, object value)>> listOfLists, List<string> subListNames = null)
    {
        StringBuilder outputString = new StringBuilder();
        outputString.AppendLine($"{listName} <- list()");
        if (subListNames != null && subListNames.Count == listOfLists.Count)
        {
            for (int i = 0; i < listOfLists.Count; i++)
            {
                outputString.Append(WriteRList($"{listName}$`{subListNames[i]}`", listOfLists[i]));
            }
        }
        else
        {
            for (int i = 0; i < listOfLists.Count; i++)
            {
                outputString.Append(WriteRList($"{listName}[[{i + 1}]]", listOfLists[i]));
            }
        }
        return outputString.ToString();
    }
    /// <summary>
    /// Generates R code for a list structure and returns it as a string.
    /// This method creates R code that defines a named list with the specified entries.
    /// Keys are wrapped in backticks to handle special characters, and values are quoted as strings.
    /// The method builds the R list definition using a StringBuilder and returns the complete R code as a string.
    /// </summary>
    /// <param name="listName">The name of the R list variable to create.</param>
    /// <param name="entries">A list of key-value pairs to be written as R list elements.</param>
    public static string WriteRList(string listName, List<(string key, object value)> entries)
    {
        StringBuilder outputString = new StringBuilder();
        // outputString.AppendLine($"{listName} <- list()");
        outputString.Append($"{listName} <- list(");
        for (int i = 0; i < entries.Count; i++)
        {
            var value = entries[i].value;
            var formattedValue = value is string ? $"\"{value}\"" : value.ToString();
            outputString.Append($"`{entries[i].key}` = {formattedValue}");
            if (i < entries.Count - 1)
            {
                outputString.Append(", ");
            }
        }
        outputString.AppendLine(")");
        return outputString.ToString();
    }
}

public class RVector<T>
{
    List<T> items { get; set; } = new List<T>();

    public static RVector<T> Create(IEnumerable<T> collection)
    {
        RVector<T> vector = new RVector<T>();
        vector.items.AddRange(collection);
        return vector;
    }

    public override string ToString()
    {
        StringBuilder outputString = new StringBuilder();
        outputString.Append("c(");
        for (int i = 0; i < items.Count; i++)
        {
            var value = items[i];
            var formattedValue = value is string ? $"\"{value}\"" : value.ToString();
            outputString.Append(formattedValue);
            if (i < items.Count - 1)
            {
                outputString.Append(", ");
            }
        }
        outputString.Append(")");
        return outputString.ToString();
    }
}