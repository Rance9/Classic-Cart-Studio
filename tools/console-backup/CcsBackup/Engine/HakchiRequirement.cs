// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// The one Hakchi setup Workbench changes: Hakchi2 CE 3.9.3 (its console components: hakchi v1.0.4-126 with kernel 3.4.113.29-madmonkey).
// A console whose Hakchi setup is anything else is refused before any write, with the same words everywhere:
// a title, what is true now, what to do, and the official release page. Workbench never installs, updates or downgrades Hakchi itself,
// never runs Hakchi's installer and never links anywhere else for it. Read-only tasks (a backup, the Console Browser) are not affected.
using System;

namespace CcsBackup.Engine
{
    public static class HakchiRequirement
    {
        public const string ConsoleVersion = Preparation.PinnedHakchiVersion;   // v1.0.4-126, what the console reports
        public const string Kernel = "3.4.113.29-madmonkey";
        public const string Url = "https://github.com/TeamShinkansen/Hakchi2-CE/releases/tag/v3.9.3";
        public const string Title = "Hakchi2 CE 3.9.3 is required";
        public const string Body = "This console's Hakchi setup does not match the version Workbench needs. Nothing has been changed.";
        public const string Next = "Install Hakchi2 CE 3.9.3 on your PC, use it to update your console, then return to Workbench.";
        public const string Button = "Get Hakchi2 CE 3.9.3";
        /// <summary>the first line of a refusal of this kind (a result page shows the notice and the button for it)</summary>
        public const string Marker = "[hakchi-3.9.3-required]";

        /// <summary>a refusal text: the marker, the plain words, then what was found (technical details)</summary>
        public static string Refusal(string detected) => Marker + "\n" + Title + "\n" + Body + "\n" + Next + "\nDetected: " + (string.IsNullOrEmpty(detected) ? "not reported" : detected);

        /// <summary>what the console reports, in words (its hakchi script version and kernel, as read)</summary>
        public static string Detected(string hakchi, string kernel = null) => (hakchi == null || hakchi == "none" ? "no Hakchi files on its data storage" : "hakchi " + hakchi) + (kernel != null ? ", kernel " + kernel : "");

        public static bool Is(string problem) => problem != null && problem.StartsWith(Marker, StringComparison.Ordinal);

        /// <summary>the detected facts of such a refusal ("" when there are none)</summary>
        public static string DetectedOf(string problem)
        {
            if (!Is(problem)) return "";
            var i = problem.IndexOf("\nDetected: ", StringComparison.Ordinal);
            return i < 0 ? "" : problem.Substring(i + "\nDetected: ".Length);
        }
    }
}
