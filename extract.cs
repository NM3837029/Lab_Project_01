using System;
using System.IO;
using System.Text.RegularExpressions;

class Program {
    static void Main() {
        string jsonl = @"C:\Users\naots\.gemini\antigravity\brain\71f2770a-a624-4b9c-9c75-4e5898b7b1d6\.system_generated\logs\transcript.jsonl";
        string maxContent = "";
        foreach (string line in File.ReadLines(jsonl)) {
            if (line.Contains("\"type\":\"VIEW_FILE\"") && line.Contains("DrawPixel.cpp")) {
                var match = Regex.Match(line, "\"content\":\"(.*?)\"}$");
                if (match.Success) {
                    string content = match.Groups[1].Value.Replace("\\n", "\n").Replace("\\r", "\r").Replace("\\\"", "\"").Replace("\\\\", "\\");
                    if (content.Length > maxContent.Length) {
                        maxContent = content;
                    }
                }
            }
        }
        File.WriteAllText("recovered_partial.txt", maxContent);
        Console.WriteLine($"Recovered length: {maxContent.Length}");
    }
}
