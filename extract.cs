using System;
using System.IO;
using System.Text.RegularExpressions;

// このプログラムは、AIエージェント（Antigravity）のログファイル（transcript.jsonl）の中から、
// "DrawPixel.cpp"を表示（VIEW_FILE）した記録を探し出し、その中で最も長い内容（content）を
// テキストファイルとして書き出すための、復旧・救出用の一回限りのツールである。
// 何らかの理由でDrawPixel.cppのソースコードが失われてしまった際に、
// 過去にAIがそのファイルを閲覧した際のログから内容を復元することを目的としている。
class Program {
    static void Main() {
        // 復元元となるログファイル（JSON Lines形式）のパス。
        // 1行が1つのJSONオブジェクトになっている形式のログファイルを想定している。
        string jsonl = @"C:\Users\naots\.gemini\antigravity\brain\71f2770a-a624-4b9c-9c75-4e5898b7b1d6\.system_generated\logs\transcript.jsonl";
        // これまでに見つかった中で最も長い（＝最も情報量が多いと推測される）content文字列を保持する変数。
        string maxContent = "";
        // ログファイルを1行ずつ読み込んで処理する（ファイル全体を一度にメモリへ読み込まないための工夫）。
        foreach (string line in File.ReadLines(jsonl)) {
            // その行が「ファイル閲覧（VIEW_FILE）」の記録であり、かつ対象がDrawPixel.cppである場合のみ処理する。
            if (line.Contains("\"type\":\"VIEW_FILE\"") && line.Contains("DrawPixel.cpp")) {
                // 正規表現を使って、JSON内の"content":"..."の部分（ファイルの中身に相当する文字列）を抜き出す。
                var match = Regex.Match(line, "\"content\":\"(.*?)\"}$");
                if (match.Success) {
                    // JSON文字列としてエスケープされている改行やバックスラッシュ、ダブルクォートを、
                    // 元の文字（\n→改行、\r→復帰、\"→"、\\→\）に戻す。
                    string content = match.Groups[1].Value.Replace("\\n", "\n").Replace("\\r", "\r").Replace("\\\"", "\"").Replace("\\\\", "\\");
                    // これまで見つけた中で最も長い内容よりも今回の内容の方が長ければ、
                    // より完全なファイル内容である可能性が高いと判断して更新する。
                    if (content.Length > maxContent.Length) {
                        maxContent = content;
                    }
                }
            }
        }
        // 最終的に見つかった最長のcontentを、復元結果としてファイルに書き出す。
        File.WriteAllText("recovered_partial.txt", maxContent);
        // 復元できた文字数をコンソールに表示し、どの程度復元できたかを確認できるようにする。
        Console.WriteLine($"Recovered length: {maxContent.Length}");
    }
}
